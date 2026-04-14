namespace Application.Features.DriverPerformance;

public sealed record DriverPerformanceDto(
    int DriverId,
    string DriverName,
    string Team,
    decimal AverageLapTimeSeconds,
    decimal DeltaToTeammateSeconds);
