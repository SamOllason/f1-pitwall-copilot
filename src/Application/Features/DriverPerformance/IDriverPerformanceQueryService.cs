namespace Application.Features.DriverPerformance;

public interface IDriverPerformanceQueryService
{
    Task<IReadOnlyList<DriverPerformanceDto>> GetOverviewAsync(CancellationToken cancellationToken = default);
}
