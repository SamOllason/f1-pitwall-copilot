namespace Application.Features.DriverPerformance;

public sealed record DriverPerformanceDashboardDto(
    IReadOnlyList<DriverFilterOptionDto> Drivers,
    IReadOnlyList<RaceFilterOptionDto> Races,
    IReadOnlyList<DriverPerformanceDto> Rows,
    IReadOnlyList<DriverRaceTrendPointDto> Trend);

public sealed record DriverFilterOptionDto(int DriverId, string DriverName, string Team);

public sealed record RaceFilterOptionDto(int RaceId, string RaceName, DateOnly Date);

public sealed record DriverRaceTrendPointDto(
    int RaceId,
    string RaceName,
    DateOnly RaceDate,
    decimal AverageLapTimeSeconds);
