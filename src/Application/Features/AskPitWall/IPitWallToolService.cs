namespace Application.Features.AskPitWall;

public interface IPitWallToolService
{
    Task<IReadOnlyList<DriverNameMatchDto>> FindDriversByNameAsync(
        string query,
        CancellationToken cancellationToken = default);

    Task<DriverPerformanceToolResultDto?> GetDriverPerformanceAsync(
        int driverId,
        CancellationToken cancellationToken = default);

    Task<DriverComparisonToolResultDto?> CompareDriversAsync(
        int driverAId,
        int driverBId,
        CancellationToken cancellationToken = default);
}
