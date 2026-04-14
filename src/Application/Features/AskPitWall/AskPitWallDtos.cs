namespace Application.Features.AskPitWall;

public sealed record AskPitWallResponseDto(
    string RequestId,
    string Answer,
    string WhySummary,
    IReadOnlyList<string> ToolCalls,
    IReadOnlyList<string> SourceMetrics,
    IReadOnlyList<AskPitWallAuditEntryDto> AuditTrail,
    int LatencyMs,
    int? PromptTokens,
    int? CompletionTokens,
    int? TotalTokens,
    bool UsedFallback);

public sealed record AskPitWallAuditEntryDto(
    string Stage,
    string Decision,
    string Detail);

public sealed record DriverPerformanceToolResultDto(
    int DriverId,
    string DriverName,
    string Team,
    decimal AverageLapTimeSeconds,
    decimal DeltaToTeammateSeconds,
    decimal ConsistencyStdDevSeconds,
    int RaceCount);

public sealed record DriverComparisonToolResultDto(
    DriverPerformanceToolResultDto DriverA,
    DriverPerformanceToolResultDto DriverB,
    decimal AverageLapGapSeconds,
    string MoreConsistentDriverName);

public sealed record DriverNameMatchDto(
    int DriverId,
    string DriverName,
    string Team,
    decimal MatchScore);
