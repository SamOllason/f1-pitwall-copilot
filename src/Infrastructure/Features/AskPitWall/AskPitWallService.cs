using System.Diagnostics;
using Application.Features.AskPitWall;
using Application.Features.DriverPerformance;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Features.AskPitWall;

public sealed class AskPitWallService(
    IDriverPerformanceQueryService driverPerformanceQueryService,
    IPitWallToolService toolService,
    ILogger<AskPitWallService> logger) : IAskPitWallService
{
    public async Task<AskPitWallResponseDto> AskAsync(string question, CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var stopwatch = Stopwatch.StartNew();
        var auditTrail = new List<AskPitWallAuditEntryDto>();
        var trimmed = question.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return new AskPitWallResponseDto(
                RequestId: requestId,
                Answer: "Ask a question about driver pace, teammate delta, or consistency.",
                WhySummary: "No analysis was run because the question was empty.",
                ToolCalls: [],
                SourceMetrics: [],
                AuditTrail: [],
                LatencyMs: (int)stopwatch.ElapsedMilliseconds,
                PromptTokens: null,
                CompletionTokens: null,
                TotalTokens: null,
                UsedFallback: false);
        }

        try
        {
            auditTrail.Add(new AskPitWallAuditEntryDto("Input", "Question parsed", $"Question: {trimmed}"));
            var overview = await driverPerformanceQueryService.GetOverviewAsync(cancellationToken);
            var mentionedDrivers = overview
                .Where(x => trimmed.Contains(x.DriverName, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.DriverId)
                .Distinct()
                .ToList();
            auditTrail.Add(new AskPitWallAuditEntryDto(
                "Intent",
                "Detected candidate drivers",
                mentionedDrivers.Count == 0 ? "No explicit driver name found." : $"Driver ids: {string.Join(", ", mentionedDrivers)}"));

            if (mentionedDrivers.Count >= 2 || ContainsComparisonIntent(trimmed))
            {
                var selected = mentionedDrivers.Count >= 2
                    ? mentionedDrivers.Take(2).ToArray()
                    : overview.Take(2).Select(x => x.DriverId).ToArray();
                auditTrail.Add(new AskPitWallAuditEntryDto(
                    "Decision",
                    "Use CompareDrivers tool",
                    $"Selected ids: {selected[0]} and {selected[1]}"));

                var comparison = await toolService.CompareDriversAsync(selected[0], selected[1], cancellationToken);
                if (comparison is not null)
                {
                    var fasterDriver = comparison.AverageLapGapSeconds <= 0m
                        ? comparison.DriverA.DriverName
                        : comparison.DriverB.DriverName;
                    var fasterBy = Math.Abs(comparison.AverageLapGapSeconds);
                    var response = new AskPitWallResponseDto(
                        RequestId: requestId,
                        Answer: $"{fasterDriver} is quicker on average by {fasterBy:0.###}s. " +
                                $"{comparison.MoreConsistentDriverName} is more consistent across races.",
                        WhySummary: "This answer compares both drivers on average pace and consistency, then explains who is faster and steadier.",
                        ToolCalls: [$"CompareDrivers({comparison.DriverA.DriverId}, {comparison.DriverB.DriverId})"],
                        SourceMetrics:
                        [
                            $"{comparison.DriverA.DriverName}: avg {comparison.DriverA.AverageLapTimeSeconds:0.###}s, stddev {comparison.DriverA.ConsistencyStdDevSeconds:0.###}s",
                            $"{comparison.DriverB.DriverName}: avg {comparison.DriverB.AverageLapTimeSeconds:0.###}s, stddev {comparison.DriverB.ConsistencyStdDevSeconds:0.###}s",
                            $"Gap (A-B): {comparison.AverageLapGapSeconds:0.###}s"
                        ],
                        AuditTrail:
                        [
                            ..auditTrail,
                            new AskPitWallAuditEntryDto("Result", "Comparison complete", $"Faster: {fasterDriver}. More consistent: {comparison.MoreConsistentDriverName}.")
                        ],
                        LatencyMs: (int)stopwatch.ElapsedMilliseconds,
                        PromptTokens: null,
                        CompletionTokens: null,
                        TotalTokens: null,
                        UsedFallback: false);
                    LogRequest(requestId, trimmed, response);
                    return response;
                }
            }

            var targetDriverId = mentionedDrivers.FirstOrDefault();
            if (targetDriverId == 0)
            {
                targetDriverId = overview.FirstOrDefault()?.DriverId ?? 0;
            }
            auditTrail.Add(new AskPitWallAuditEntryDto(
                "Decision",
                "Use GetDriverPerformance tool",
                $"Chosen driver id: {targetDriverId}"));

            var driver = await toolService.GetDriverPerformanceAsync(targetDriverId, cancellationToken);
            if (driver is not null)
            {
                var response = new AskPitWallResponseDto(
                    RequestId: requestId,
                    Answer: $"{driver.DriverName} averages {driver.AverageLapTimeSeconds:0.###}s per lap. " +
                            $"Delta to teammate is {driver.DeltaToTeammateSeconds:0.###}s and consistency stddev is {driver.ConsistencyStdDevSeconds:0.###}s.",
                    WhySummary: "This answer uses one-driver pace, teammate delta, and consistency to explain performance.",
                    ToolCalls: [$"GetDriverPerformance({driver.DriverId})"],
                    SourceMetrics:
                    [
                        $"{driver.DriverName}: avg {driver.AverageLapTimeSeconds:0.###}s",
                        $"Delta to teammate: {driver.DeltaToTeammateSeconds:0.###}s",
                        $"Consistency stddev: {driver.ConsistencyStdDevSeconds:0.###}s over {driver.RaceCount} races"
                    ],
                    AuditTrail:
                    [
                        ..auditTrail,
                        new AskPitWallAuditEntryDto("Result", "Single-driver summary complete", $"Driver: {driver.DriverName}.")
                    ],
                    LatencyMs: (int)stopwatch.ElapsedMilliseconds,
                    PromptTokens: null,
                    CompletionTokens: null,
                    TotalTokens: null,
                    UsedFallback: false);
                LogRequest(requestId, trimmed, response);
                return response;
            }
        }
        catch
        {
            // Fall back to deterministic metrics only if tool orchestration fails.
        }

        var fallback = await BuildFallbackAsync(requestId, stopwatch, cancellationToken);
        LogRequest(requestId, trimmed, fallback);
        return fallback;
    }

    private async Task<AskPitWallResponseDto> BuildFallbackAsync(
        string requestId,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var overview = await driverPerformanceQueryService.GetOverviewAsync(cancellationToken);
        var top = overview.Take(3).ToList();
        var answer = top.Count == 0
            ? "No performance data is available yet."
            : $"AI tool path failed, so here are raw stats. Best average pace: {top[0].DriverName} at {top[0].AverageLapTimeSeconds:0.###}s.";

        var metrics = top
            .Select(x => $"{x.DriverName}: avg {x.AverageLapTimeSeconds:0.###}s, delta {x.DeltaToTeammateSeconds:0.###}s")
            .ToList();

        return new AskPitWallResponseDto(
            RequestId: requestId,
            Answer: answer,
            WhySummary: "Fallback mode was used to return deterministic stats when AI or tool orchestration could not complete.",
            ToolCalls: [],
            SourceMetrics: metrics,
            AuditTrail:
            [
                new AskPitWallAuditEntryDto("Fallback", "Returned deterministic data", "Top drivers were selected from precomputed overview stats.")
            ],
            LatencyMs: (int)stopwatch.ElapsedMilliseconds,
            PromptTokens: null,
            CompletionTokens: null,
            TotalTokens: null,
            UsedFallback: true);
    }

    private void LogRequest(string requestId, string question, AskPitWallResponseDto response)
    {
        logger.LogInformation(
            "AskPitWall request {RequestId} question=\"{Question}\" tools={ToolCalls} latencyMs={LatencyMs} fallback={UsedFallback} answer=\"{Answer}\"",
            requestId,
            question,
            string.Join(", ", response.ToolCalls),
            response.LatencyMs,
            response.UsedFallback,
            response.Answer);
    }

    private static bool ContainsComparisonIntent(string question)
    {
        return question.Contains("compare", StringComparison.OrdinalIgnoreCase)
            || question.Contains("vs", StringComparison.OrdinalIgnoreCase)
            || question.Contains("versus", StringComparison.OrdinalIgnoreCase)
            || question.Contains("more consistent", StringComparison.OrdinalIgnoreCase);
    }
}
