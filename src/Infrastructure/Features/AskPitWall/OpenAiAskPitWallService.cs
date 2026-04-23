using System.Diagnostics;
using System.Text.Json;
using Application.Features.AskPitWall;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
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

    // Entry point — keeps orchestration concerns separate from AI logic.
    //
    // Flow:
    //   1. No API key         → skip AI, return deterministic fallback immediately.
    //   2. AI path succeeds   → return the LLM-composed response.
    //   3. AI path throws     → log the error, return deterministic fallback with UsedFallback:true.
    public async Task<AskPitWallResponseDto> AskAsync(string question, CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var stopwatch = Stopwatch.StartNew();

        // No API key — skip AI entirely, go straight to the deterministic service.
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return await fallbackService.AskAsync(question, cancellationToken);
        }

        try
        {
            return await RunAiPathAsync(question, requestId, stopwatch, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OpenAI path failed for request {RequestId}, falling back to deterministic service.", requestId);
        }

        var fallback = await fallbackService.AskAsync(question, cancellationToken);
        var fallbackWithFlag = fallback with
        {
            UsedFallback = true,
            Confidence = new AnswerConfidence(ConfidenceLevel.VeryLow, "OpenAI path failed — answer is from deterministic fallback only")
        };
        LogRequest(requestId, question, fallbackWithFlag);
        return fallbackWithFlag;
    }

    // Runs the full AI path:
    //   1. Retrieve RAG context and build the system prompt.
    //   2. Run the agentic loop (up to 4 turns): call the LLM, dispatch any tool calls, feed
    //      results back into the message history, repeat until the model stops requesting tools.
    //   3. Return a response DTO built from the final assistant message.
    private async Task<AskPitWallResponseDto> RunAiPathAsync(
        string question,
        string requestId,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var chatClient = new AzureOpenAIClient(new Uri(settings.Endpoint!), new AzureKeyCredential(settings.ApiKey!))
            .GetChatClient(settings.Model);
        var toolCalls = new List<string>();
        var sourceMetrics = new List<string>();
        var chunkScores = new List<double>(); // collected across upfront retrieval + any SearchRagContext tool calls
        var promptTokens = 0;
        var completionTokens = 0;
        var totalTokens = 0;

        // 1. Fetch relevant document chunks and wire them into the system prompt.
        var (messages, auditTrail) = await BuildInitialMessagesAsync(question, sourceMetrics, chunkScores, cancellationToken);

        // 2. Agentic loop — THIS IS WHERE THE MODEL DECIDES WHAT TO DO.
        //
        // How tool-calling works:
        //   - We pass the model a list of available tools (defined in BuildOptions).
        //   - The model reads the question + RAG context and decides whether it needs more
        //     evidence. If it does, it responds with one or more tool call requests instead
        //     of a final answer. It does NOT call the tools itself — it just says "please
        //     call X with these arguments". We execute those calls and send the results back.
        //   - On the next turn the model has new evidence and can either ask for more tools
        //     or produce its final answer.
        //   - This repeats up to 4 turns. In practice most questions settle in 1–2 turns.
        //
        // The tool the model picks depends on the question type:
        //   - Factual race/setup/strategy questions → SearchRagContext (retrieves doc chunks)
        //   - "Who is faster?" / pace questions     → GetDriverPerformance or CompareDrivers
        //   - Ambiguous driver name in the question → FindDriversByName first, then metrics
        //   - Complex questions                     → may combine retrieval + metrics tools
        var options = BuildOptions();
        for (var i = 0; i < 4; i++)
        {
            // Send the full conversation history (system prompt + RAG context + any prior
            // tool results) to the model and wait for its next response.
            var completion = await chatClient.CompleteChatAsync(messages, options, cancellationToken);
            var assistantMessage = completion.Value;
            promptTokens += assistantMessage.Usage.InputTokenCount;
            completionTokens += assistantMessage.Usage.OutputTokenCount;
            totalTokens += assistantMessage.Usage.TotalTokenCount;

            // Append the model's response to the history so the next turn has full context.
            messages.Add(new AssistantChatMessage(assistantMessage));

            // ── DECISION POINT ──────────────────────────────────────────────────────────
            // The model either requests more tools OR produces its final text answer.
            // ────────────────────────────────────────────────────────────────────────────

            if (assistantMessage.ToolCalls.Count == 0)
            {
                // No tool calls → the model is satisfied it has enough evidence.
                // Extract the answer text and build the response DTO.
                var finalAnswer = assistantMessage.Content.Count > 0
                    ? string.Join(" ", assistantMessage.Content.Select(x => x.Text))
                    : "I could not generate a response.";

                // 3. Evaluate confidence from observable signals, then build and return the response.
                var confidence = ConfidenceEvaluator.Evaluate(chunkScores, toolCalls.Count, finalAnswer);
                var response = new AskPitWallResponseDto(
                    RequestId: requestId,
                    Answer: finalAnswer,
                    WhySummary: "This answer was composed by the LLM from retrieved context and tool outputs gathered during this run.",
                    ToolCalls: toolCalls,
                    SourceMetrics: sourceMetrics,
                    AuditTrail:
                    [
                        ..auditTrail,
                        new AskPitWallAuditEntryDto("Result", "Final response generated", $"Tool calls used: {toolCalls.Count}"),
                        new AskPitWallAuditEntryDto("Confidence", confidence.Level.ToString(), confidence.Rationale)
                    ],
                    LatencyMs: (int)stopwatch.ElapsedMilliseconds,
                    PromptTokens: promptTokens,
                    CompletionTokens: completionTokens,
                    TotalTokens: totalTokens,
                    UsedFallback: false,
                    Confidence: confidence);
                LogRequest(requestId, question, response);
                return response;
            }

            // The model wants more information — it has chosen one or more tools to call.
            // We execute each tool, record what happened in the audit trail, then append
            // the results as ToolChatMessages so the model can read them on the next turn.
            foreach (var toolCall in assistantMessage.ToolCalls)
            {
                auditTrail.Add(new AskPitWallAuditEntryDto(
                    "Decision",
                    $"LLM chose tool: {toolCall.FunctionName}",
                    $"Args: {toolCall.FunctionArguments}"));
                var toolResult = await ExecuteToolAsync(toolCall, toolCalls, sourceMetrics, chunkScores, auditTrail, cancellationToken);
                messages.Add(new ToolChatMessage(toolCall.Id, toolResult));
            }

            // Loop back → model now sees its own tool results and decides again.
        }

        // Reached the turn limit without a final answer — treat as a failure so AskAsync falls back.
        throw new InvalidOperationException("Agent loop exhausted all turns without producing a final answer.");
    }

    // Retrieves RAG chunks, formats them into a context block, and returns the initial message
    // list (system prompt + user question) together with the opening audit trail entries.
    private async Task<(List<ChatMessage> Messages, List<AskPitWallAuditEntryDto> AuditTrail)> BuildInitialMessagesAsync(
        string question,
        List<string> sourceMetrics,
        List<double> chunkScores,
        CancellationToken cancellationToken)
    {
        var ragChunks = await ragContextService.RetrieveAsync(question, top: 4, cancellationToken);
        chunkScores.AddRange(ragChunks.Select(x => x.Score));

        // Summarise what was retrieved for the audit trail.
        var ragAuditDetail = ragChunks.Count == 0
            ? "No chunks retrieved — answer will rely entirely on tool outputs."
            : string.Join("; ", ragChunks.Select(x => $"{x.Source} [{x.Race}/{x.Driver}, score {x.Score:0.###}]"));

        // Surface RAG sources in the UI source-metrics panel.
        if (ragChunks.Count > 0)
        {
            sourceMetrics.AddRange(ragChunks.Select(x =>
                $"RAG [{x.DocType}] {x.Race}/{x.Driver} (score {x.Score:0.###}) -> {x.Source}"));
        }

        // Format chunks into a numbered block that sits in the system prompt.
        var ragContextBlock = ragChunks.Count == 0
            ? "No additional retrieval context was found."
            : string.Join(
                "\n\n",
                ragChunks.Select((x, i) =>
                    $"[Chunk {i + 1}] source={x.Source}, race={x.Race}, driver={x.Driver}, circuit={x.Circuit}\n{x.Content}"));

        var auditTrail = new List<AskPitWallAuditEntryDto>
        {
            new("Input", "Question submitted to LLM", question),
            new("Policy", "Grounding policy", "Answer from retrieved context + tool outputs; never fabricate facts."),
            new("RAG", $"Retrieved {ragChunks.Count} contextual chunk(s) — injected into system prompt", ragAuditDetail)
        };

        // One entry per chunk so the user can see exactly what context the LLM was given.
        AddChunkAuditEntries(auditTrail, ragChunks, "RAG Chunk (upfront)");

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

        return (messages, auditTrail);
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
        List<double> chunkScores,
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
                $"SearchRagContext returned {chunks.Count} chunk(s)",
                $"Query: \"{query}\", top source: {chunks[0].Source}"));

            // Collect scores so the confidence evaluator sees tool-triggered retrieval too.
            chunkScores.AddRange(chunks.Select(x => x.Score));

            // One entry per chunk so the user can see what the LLM retrieved on-demand.
            AddChunkAuditEntries(auditTrail, chunks, "RAG Chunk (tool)");
            return JsonSerializer.Serialize(chunks, JsonOptions);
        }

        return """{"error":"Unknown function."}""";
    }

    // Appends one audit entry per chunk, showing the source, relevance score, and a short
    // content preview. The stage label distinguishes upfront retrieval from tool-triggered
    // retrieval so the user can see when the LLM decided it needed more context mid-turn.
    private static void AddChunkAuditEntries(
        List<AskPitWallAuditEntryDto> auditTrail,
        IReadOnlyList<RagContextChunkDto> chunks,
        string stage)
    {
        foreach (var (chunk, index) in chunks.Select((c, i) => (c, i + 1)))
        {
            var preview = chunk.Content.Length <= 120
                ? chunk.Content
                : chunk.Content[..120] + "…";
            auditTrail.Add(new AskPitWallAuditEntryDto(
                stage,
                $"#{index} {chunk.Source} — {chunk.Race} / {chunk.Driver} (score {chunk.Score:0.###})",
                preview));
        }
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
