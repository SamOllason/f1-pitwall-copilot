namespace Application.Features.DriverPerformance;

public interface IDriverPerformanceQueryService
{
    Task<IReadOnlyList<DriverPerformanceDto>> GetOverviewAsync(CancellationToken cancellationToken = default);
    Task<DriverPerformanceDashboardDto> GetDashboardAsync(
        int? driverId,
        int? raceId,
        CancellationToken cancellationToken = default);
}
