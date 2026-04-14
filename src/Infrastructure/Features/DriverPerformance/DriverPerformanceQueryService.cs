using Application.Features.DriverPerformance;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Features.DriverPerformance;

public sealed class DriverPerformanceQueryService(PitWallDbContext dbContext) : IDriverPerformanceQueryService
{
    public async Task<IReadOnlyList<DriverPerformanceDto>> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var dashboard = await GetDashboardAsync(driverId: null, raceId: null, cancellationToken);
        return dashboard.Rows;
    }

    public async Task<DriverPerformanceDashboardDto> GetDashboardAsync(
        int? driverId,
        int? raceId,
        CancellationToken cancellationToken = default)
    {
        var drivers = await dbContext.Drivers
            .OrderBy(x => x.Name)
            .Select(x => new DriverFilterOptionDto(x.Id, x.Name, x.Team))
            .ToListAsync(cancellationToken);

        var races = await dbContext.Races
            .OrderBy(x => x.Date)
            .Select(x => new RaceFilterOptionDto(x.Id, x.Name, x.Date))
            .ToListAsync(cancellationToken);

        var baselineQuery = dbContext.LapSummaries
            .AsNoTracking()
            .Include(x => x.Driver)
            .Include(x => x.Race)
            .AsQueryable();

        if (raceId is not null)
        {
            baselineQuery = baselineQuery.Where(x => x.RaceId == raceId.Value);
        }

        var baselineRows = await baselineQuery
            .Select(x => new
            {
                x.DriverId,
                DriverName = x.Driver.Name,
                x.Driver.Team,
                x.RaceId,
                RaceName = x.Race.Name,
                x.Race.Date,
                x.AverageLapTimeSeconds
            })
            .ToListAsync(cancellationToken);

        var avgByDriver = baselineRows
            .GroupBy(x => new { x.DriverId, x.DriverName, x.Team })
            .ToDictionary(
                x => x.Key.DriverId,
                x => new
                {
                    x.Key.DriverName,
                    x.Key.Team,
                    AverageLap = x.Average(y => y.AverageLapTimeSeconds)
                });

        var rows = avgByDriver
            .Where(x => driverId is null || x.Key == driverId.Value)
            .Select(x =>
            {
                var teammateAverage = avgByDriver
                    .Where(y => y.Value.Team == x.Value.Team && y.Key != x.Key)
                    .Select(y => y.Value.AverageLap)
                    .DefaultIfEmpty(x.Value.AverageLap)
                    .Average();

                return new DriverPerformanceDto(
                    DriverId: x.Key,
                    DriverName: x.Value.DriverName,
                    Team: x.Value.Team,
                    AverageLapTimeSeconds: decimal.Round(x.Value.AverageLap, 3),
                    DeltaToTeammateSeconds: decimal.Round(x.Value.AverageLap - teammateAverage, 3));
            })
            .OrderBy(x => x.AverageLapTimeSeconds)
            .ToList();

        var trendDriverId = driverId ?? rows.FirstOrDefault()?.DriverId;
        var trend = baselineRows
            .Where(x => trendDriverId is not null && x.DriverId == trendDriverId.Value)
            .GroupBy(x => new { x.RaceId, x.RaceName, x.Date })
            .Select(x => new DriverRaceTrendPointDto(
                RaceId: x.Key.RaceId,
                RaceName: x.Key.RaceName,
                RaceDate: x.Key.Date,
                AverageLapTimeSeconds: decimal.Round(x.Average(y => y.AverageLapTimeSeconds), 3)))
            .OrderBy(x => x.RaceDate)
            .ToList();

        return new DriverPerformanceDashboardDto(drivers, races, rows, trend);
    }
}
