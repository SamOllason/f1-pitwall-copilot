using Application.Features.DriverPerformance;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Features.DriverPerformance;

public sealed class DriverPerformanceQueryService(PitWallDbContext dbContext) : IDriverPerformanceQueryService
{
    public async Task<IReadOnlyList<DriverPerformanceDto>> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var averages = await dbContext.LapSummaries
            .GroupBy(x => x.DriverId)
            .Select(group => new
            {
                DriverId = group.Key,
                AvgLap = group.Average(x => x.AverageLapTimeSeconds)
            })
            .ToListAsync(cancellationToken);

        var drivers = await dbContext.Drivers.ToListAsync(cancellationToken);
        var byId = averages.ToDictionary(x => x.DriverId, x => x.AvgLap);

        var result = new List<DriverPerformanceDto>(drivers.Count);
        foreach (var driver in drivers)
        {
            byId.TryGetValue(driver.Id, out var avgLap);
            
            // get this so can compare to current driver avgLap time below
            var teammateAverageLap = drivers
                .Where(d => d.Team == driver.Team && d.Id != driver.Id)
                .Select(d => byId.TryGetValue(d.Id, out var value) ? value : avgLap)
                .DefaultIfEmpty(avgLap)
                .Average();

            result.Add(new DriverPerformanceDto(
                driver.Id,
                driver.Name,
                driver.Team,
                decimal.Round(avgLap, 3),
                decimal.Round(avgLap - teammateAverageLap, 3)));
        }

        return result.OrderBy(x => x.AverageLapTimeSeconds).ToList();
    }
}
