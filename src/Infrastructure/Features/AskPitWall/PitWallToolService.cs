using Application.Features.AskPitWall;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Features.AskPitWall;

public sealed class PitWallToolService(PitWallDbContext dbContext) : IPitWallToolService
{
    public async Task<IReadOnlyList<DriverNameMatchDto>> FindDriversByNameAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = Normalize(query);
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return [];
        }

        var drivers = await dbContext.Drivers
            .AsNoTracking()
            .Select(x => new { x.Id, x.Name, x.Team })
            .ToListAsync(cancellationToken);

        var matches = drivers
            .Select(x =>
            {
                var normalizedName = Normalize(x.Name);
                var score = ScoreMatch(normalizedQuery, normalizedName, x.Name);
                return new { x.Id, x.Name, x.Team, Score = score };
            })
            .Where(x => x.Score > 0m)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Name)
            .Take(5)
            .Select(x => new DriverNameMatchDto(x.Id, x.Name, x.Team, decimal.Round(x.Score, 3)))
            .ToList();

        return matches;
    }

    public async Task<DriverPerformanceToolResultDto?> GetDriverPerformanceAsync(
        int driverId,
        CancellationToken cancellationToken = default)
    {
        var data = await dbContext.LapSummaries
            .AsNoTracking()
            .Include(x => x.Driver)
            .Where(x => x.DriverId == driverId)
            .Select(x => new
            {
                x.DriverId,
                DriverName = x.Driver.Name,
                x.Driver.Team,
                x.AverageLapTimeSeconds
            })
            .ToListAsync(cancellationToken);

        if (data.Count == 0)
        {
            return null;
        }

        var averageLap = data.Average(x => x.AverageLapTimeSeconds);
        var consistencyStdDev = CalculateStdDev(data.Select(x => x.AverageLapTimeSeconds).ToList());

        var teammateData = await dbContext.LapSummaries
            .AsNoTracking()
            .Include(x => x.Driver)
            .Where(x => x.Driver.Team == data[0].Team && x.DriverId != driverId)
            .Select(x => x.AverageLapTimeSeconds)
            .ToListAsync(cancellationToken);

        var teammateAverage = teammateData.Count > 0
            ? teammateData.Average()
            : averageLap;

        return new DriverPerformanceToolResultDto(
            DriverId: data[0].DriverId,
            DriverName: data[0].DriverName,
            Team: data[0].Team,
            AverageLapTimeSeconds: decimal.Round(averageLap, 3),
            DeltaToTeammateSeconds: decimal.Round(averageLap - teammateAverage, 3),
            ConsistencyStdDevSeconds: decimal.Round(consistencyStdDev, 3),
            RaceCount: data.Count);
    }

    public async Task<DriverComparisonToolResultDto?> CompareDriversAsync(
        int driverAId,
        int driverBId,
        CancellationToken cancellationToken = default)
    {
        var driverA = await GetDriverPerformanceAsync(driverAId, cancellationToken);
        var driverB = await GetDriverPerformanceAsync(driverBId, cancellationToken);

        if (driverA is null || driverB is null)
        {
            return null;
        }

        var moreConsistentDriver = driverA.ConsistencyStdDevSeconds <= driverB.ConsistencyStdDevSeconds
            ? driverA.DriverName
            : driverB.DriverName;

        return new DriverComparisonToolResultDto(
            DriverA: driverA,
            DriverB: driverB,
            AverageLapGapSeconds: decimal.Round(driverA.AverageLapTimeSeconds - driverB.AverageLapTimeSeconds, 3),
            MoreConsistentDriverName: moreConsistentDriver);
    }

    private static decimal CalculateStdDev(IReadOnlyList<decimal> values)
    {
        if (values.Count <= 1)
        {
            return 0m;
        }

        var average = values.Average();
        var variance = values
            .Select(x =>
            {
                var delta = x - average;
                return delta * delta;
            })
            .Average();

        return (decimal)Math.Sqrt((double)variance);
    }

    private static string Normalize(string value)
    {
        return new string(value
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
    }

    private static decimal ScoreMatch(string query, string candidate, string originalName)
    {
        if (candidate == query)
        {
            return 1.0m;
        }

        if (candidate.StartsWith(query, StringComparison.Ordinal))
        {
            return 0.93m;
        }

        if (candidate.Contains(query, StringComparison.Ordinal))
        {
            return 0.85m;
        }

        // Token prefix matching handles short names like "lando".
        var splitTokens = originalName
            .ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(Normalize);
        if (splitTokens.Any(t => t.StartsWith(query, StringComparison.Ordinal)))
        {
            return 0.8m;
        }

        return 0m;
    }
}
