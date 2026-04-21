using System.Diagnostics;
using System.Text.Json;
using Application.Features.AskPitWall;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;

namespace Infrastructure.Features.AskPitWall;

public sealed class OpenAiAskPitWallService(
    OpenAiSettings settings,
    IPitWallToolService toolService,
    IRagContextService ragContextService,
    AskPitWallService fallbackService,
    ILogger<OpenAiAskPitWallService> logger) : IAskPitWallService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AskPitWallResponseDto> AskAsync(string question, CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var stopwatch = Stopwatch.StartNew();
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return await fallbackService.AskAsync(question, cancellationToken);
        }

        try
        {
            var client = new OpenAIClient(settings.ApiKey);
            var chatClient = client.GetChatClient(settings.Model);

            var toolCalls = new List<string>();
            var sourceMetrics = new List<string>();
            var promptTokens = 0;
            var completionTokens = 0;
            var totalTokens = 0;
            var ragChunks = await ragContextService.RetrieveAsync(question, top: 4, cancellationToken);
            var auditTrail = new List<AskPitWallAuditEntryDto>
            {
                new("Input", "Question submitted to LLM", question),
                new("Policy", "Tool-only grounding enabled", "Model is instructed to answer only from tool outputs."),
                new("RAG", "Retrieved contextual docs", $"Chunks retrieved: {ragChunks.Count}")
            };

            if (ragChunks.Count > 0)
            {
                sourceMetrics.AddRange(ragChunks.Select(x =>
                    $"RAG [{x.DocType}] {x.Race}/{x.Driver} (score {x.Score:0.###}) -> {x.Source}"));
            }

            var ragContextBlock = ragChunks.Count == 0
                ? "No additional retrieval context was found."
                : string.Join(
                    "\n\n",
                    ragChunks.Select((x, i) =>
                        $"[Chunk {i + 1}] source={x.Source}, race={x.Race}, driver={x.Driver}, circuit={x.Circuit}\n{x.Content}"));

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(
                    "You are PitWall. Ground every answer in retrieved context and/or tool outputs; never fabricate facts or ids. " +
                    "Decision rubric: use retrieval when context is required, use metrics tools when numeric driver performance is required, and combine both when needed. " +
                    "If user names are ambiguous, resolve them with FindDriversByName before any id-based tool call. " +
                    "Before finalizing, verify the answer is evidence-backed and cite source paths when context is used."),
                new SystemChatMessage($"Retrieved context:\n{ragContextBlock}"),
                new UserChatMessage(question)
            };

            var options = BuildOptions();
            for (var i = 0; i < 4; i++)
            {
                var completion = await chatClient.CompleteChatAsync(messages, options, cancellationToken);
                var assistantMessage = completion.Value;
                promptTokens += assistantMessage.Usage.InputTokenCount;
                completionTokens += assistantMessage.Usage.OutputTokenCount;
                totalTokens += assistantMessage.Usage.TotalTokenCount;
                messages.Add(new AssistantChatMessage(assistantMessage));

                if (assistantMessage.ToolCalls.Count == 0)
                {
                    var finalAnswer = assistantMessage.Content.Count > 0
                        ? string.Join(" ", assistantMessage.Content.Select(x => x.Text))
                        : "I could not generate a response.";
                    var response = new AskPitWallResponseDto(
                        RequestId: requestId,
                        Answer: finalAnswer,
                        WhySummary: "The LLM used tool outputs collected during this run to compose the explanation.",
                        ToolCalls: toolCalls,
                        SourceMetrics: sourceMetrics,
                        AuditTrail:
                        [
                            ..auditTrail,
                            new AskPitWallAuditEntryDto("Result", "Final response generated", $"Tool calls used: {toolCalls.Count}")
                        ],
                        LatencyMs: (int)stopwatch.ElapsedMilliseconds,
                        PromptTokens: promptTokens,
                        CompletionTokens: completionTokens,
                        TotalTokens: totalTokens,
                        UsedFallback: false);
                    LogRequest(requestId, question, response);
                    return response;
                }

                foreach (var toolCall in assistantMessage.ToolCalls)
                {
                    auditTrail.Add(new AskPitWallAuditEntryDto(
                        "Decision",
                        "LLM requested tool",
                        $"{toolCall.FunctionName} with args {toolCall.FunctionArguments.ToString()}"));
                    var toolResult = await ExecuteToolAsync(toolCall, toolCalls, sourceMetrics, auditTrail, cancellationToken);
                    messages.Add(new ToolChatMessage(toolCall.Id, toolResult));
                }
            }
        }
        catch
        {
            // Use deterministic fallback on AI/tool-call failures.
        }

        var fallback = await fallbackService.AskAsync(question, cancellationToken);
        LogRequest(requestId, question, fallback);
        return fallback;
    }

    private static ChatCompletionOptions BuildOptions()
    {
        var options = new ChatCompletionOptions();
        options.Tools.Add(ChatTool.CreateFunctionTool(
            functionName: "FindDriversByName",
            functionDescription: "Resolve user-provided driver names to stable driver ids before id-based metric calls.",
            functionParameters: BinaryData.FromString("""
            {
              "type": "object",
              "properties": {
                "query": { "type": "string" }
              },
              "required": ["query"]
            }
            """)));

        options.Tools.Add(ChatTool.CreateFunctionTool(
            functionName: "GetDriverPerformance",
            functionDescription: "Get deterministic pace/consistency metrics for one driver when a numeric driver analysis is needed.",
            functionParameters: BinaryData.FromString("""
            {
              "type": "object",
              "properties": {
                "driverId": { "type": "integer" }
              },
              "required": ["driverId"]
            }
            """)));

        options.Tools.Add(ChatTool.CreateFunctionTool(
            functionName: "CompareDrivers",
            functionDescription: "Compare two drivers on deterministic pace and consistency metrics.",
            functionParameters: BinaryData.FromString("""
            {
              "type": "object",
              "properties": {
                "driverAId": { "type": "integer" },
                "driverBId": { "type": "integer" }
              },
              "required": ["driverAId", "driverBId"]
            }
            """)));

        options.Tools.Add(ChatTool.CreateFunctionTool(
            functionName: "SearchRagContext",
            functionDescription: "Retrieve relevant race/debrief knowledge chunks with sources when contextual evidence is required.",
            functionParameters: BinaryData.FromString("""
            {
              "type": "object",
              "properties": {
                "query": { "type": "string" },
                "top": { "type": "integer", "minimum": 1, "maximum": 8 }
              },
              "required": ["query"]
            }
            """)));

        return options;
    }

    private async Task<string> ExecuteToolAsync(
        ChatToolCall toolCall,
        List<string> toolCalls,
        List<string> sourceMetrics,
        List<AskPitWallAuditEntryDto> auditTrail,
        CancellationToken cancellationToken)
    {
        using var argsDoc = JsonDocument.Parse(toolCall.FunctionArguments);
        var root = argsDoc.RootElement;

        if (string.Equals(toolCall.FunctionName, "GetDriverPerformance", StringComparison.Ordinal))
        {
            var driverId = root.GetProperty("driverId").GetInt32();
            var result = await toolService.GetDriverPerformanceAsync(driverId, cancellationToken);
            toolCalls.Add($"GetDriverPerformance({driverId})");
            if (result is null)
            {
                auditTrail.Add(new AskPitWallAuditEntryDto("ToolResult", "GetDriverPerformance failed", "Driver not found."));
                return """{"error":"Driver not found."}""";
            }

            sourceMetrics.Add($"{result.DriverName}: avg {result.AverageLapTimeSeconds:0.###}s, delta {result.DeltaToTeammateSeconds:0.###}s");
            auditTrail.Add(new AskPitWallAuditEntryDto(
                "ToolResult",
                "GetDriverPerformance succeeded",
                $"{result.DriverName}: avg {result.AverageLapTimeSeconds:0.###}s, consistency {result.ConsistencyStdDevSeconds:0.###}s"));
            return JsonSerializer.Serialize(result, JsonOptions);
        }

        if (string.Equals(toolCall.FunctionName, "FindDriversByName", StringComparison.Ordinal))
        {
            var query = root.GetProperty("query").GetString() ?? string.Empty;
            var result = await toolService.FindDriversByNameAsync(query, cancellationToken);
            toolCalls.Add($"FindDriversByName(\"{query}\")");
            if (result.Count == 0)
            {
                auditTrail.Add(new AskPitWallAuditEntryDto("ToolResult", "FindDriversByName returned no matches", $"Query: {query}"));
                return """{"matches":[],"message":"No matching drivers found."}""";
            }

            var top = result[0];
            sourceMetrics.Add($"Best name match: {top.DriverName} ({top.Team}), score {top.MatchScore:0.###}");
            auditTrail.Add(new AskPitWallAuditEntryDto(
                "ToolResult",
                "FindDriversByName resolved candidates",
                $"Top match: {top.DriverName} (id {top.DriverId}, score {top.MatchScore:0.###})."));
            return JsonSerializer.Serialize(result, JsonOptions);
        }

        if (string.Equals(toolCall.FunctionName, "CompareDrivers", StringComparison.Ordinal))
        {
            var driverAId = root.GetProperty("driverAId").GetInt32();
            var driverBId = root.GetProperty("driverBId").GetInt32();
            var result = await toolService.CompareDriversAsync(driverAId, driverBId, cancellationToken);
            toolCalls.Add($"CompareDrivers({driverAId}, {driverBId})");
            if (result is null)
            {
                auditTrail.Add(new AskPitWallAuditEntryDto("ToolResult", "CompareDrivers failed", "One or both drivers not found."));
                return """{"error":"One or both drivers not found."}""";
            }

            sourceMetrics.Add($"{result.DriverA.DriverName}: avg {result.DriverA.AverageLapTimeSeconds:0.###}s, stddev {result.DriverA.ConsistencyStdDevSeconds:0.###}s");
            sourceMetrics.Add($"{result.DriverB.DriverName}: avg {result.DriverB.AverageLapTimeSeconds:0.###}s, stddev {result.DriverB.ConsistencyStdDevSeconds:0.###}s");
            sourceMetrics.Add($"Gap (A-B): {result.AverageLapGapSeconds:0.###}s");
            auditTrail.Add(new AskPitWallAuditEntryDto(
                "ToolResult",
                "CompareDrivers succeeded",
                $"Gap {result.AverageLapGapSeconds:0.###}s. More consistent: {result.MoreConsistentDriverName}."));
            return JsonSerializer.Serialize(result, JsonOptions);
        }

        if (string.Equals(toolCall.FunctionName, "SearchRagContext", StringComparison.Ordinal))
        {
            var query = root.GetProperty("query").GetString() ?? string.Empty;
            var top = root.TryGetProperty("top", out var topElement) && topElement.ValueKind == JsonValueKind.Number
                ? Math.Clamp(topElement.GetInt32(), 1, 8)
                : 4;
            var chunks = await ragContextService.RetrieveAsync(query, top, cancellationToken);
            toolCalls.Add($"SearchRagContext(\"{query}\", top:{top})");

            if (chunks.Count == 0)
            {
                auditTrail.Add(new AskPitWallAuditEntryDto("ToolResult", "SearchRagContext returned no chunks", $"Query: {query}"));
                return """{"chunks":[],"message":"No context found."}""";
            }

            sourceMetrics.AddRange(chunks.Select(x =>
                $"RAG [{x.DocType}] {x.Race}/{x.Driver} (score {x.Score:0.###}) -> {x.Source}"));
            auditTrail.Add(new AskPitWallAuditEntryDto(
                "ToolResult",
                "SearchRagContext returned chunks",
                $"Chunks: {chunks.Count}, top source: {chunks[0].Source}"));
            return JsonSerializer.Serialize(chunks, JsonOptions);
        }

        return """{"error":"Unknown function."}""";
    }

    private void LogRequest(string requestId, string question, AskPitWallResponseDto response)
    {
        logger.LogInformation(
            "AskPitWall request {RequestId} question=\"{Question}\" tools={ToolCalls} latencyMs={LatencyMs} tokens={TotalTokens} fallback={UsedFallback} answer=\"{Answer}\"",
            requestId,
            question,
            string.Join(", ", response.ToolCalls),
            response.LatencyMs,
            response.TotalTokens,
            response.UsedFallback,
            response.Answer);
    }
}
