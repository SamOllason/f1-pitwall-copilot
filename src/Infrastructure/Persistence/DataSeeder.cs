using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public static class DataSeeder
{
    public sealed record SeedValidationResult(
        int DriverCount,
        int RaceCount,
        int LapSummaryCount,
        int CarPartCount,
        bool HasExpectedLapSummaryCount,
        bool HasValidLapRanges,
        bool HasValidRelationships);

    public static async Task SeedAsync(PitWallDbContext dbContext, CancellationToken cancellationToken = default)
    {
        if (dbContext.Drivers.Any())
        {
            return;
        }

        var drivers = new List<Driver>
        {
            new() { Name = "Lando Norris", Team = "McLaren" },
            new() { Name = "Oscar Piastri", Team = "McLaren" },
            new() { Name = "Charles Leclerc", Team = "Ferrari" },
            new() { Name = "Lewis Hamilton", Team = "Ferrari" }
        };

        var races = Enumerable.Range(1, 10)
            .Select(i => new Race
            {
                Name = $"Race {i}",
                Circuit = i % 2 == 0 ? "Silverstone" : "Monza",
                Date = new DateOnly(2025, 3, 1).AddDays(i * 7)
            })
            .ToList();

        await dbContext.Drivers.AddRangeAsync(drivers, cancellationToken);
        await dbContext.Races.AddRangeAsync(races, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        var random = new Random(42);
        var lapSummaries = new List<LapSummary>();
        var carParts = new List<CarPart>();

        foreach (var race in races)
        {
            foreach (var driver in drivers)
            {
                var baseline = driver.Team == "McLaren" ? 88.7m : 89.2m;
                var variation = (decimal)(random.NextDouble() * 1.2 - 0.6);
                lapSummaries.Add(new LapSummary
                {
                    DriverId = driver.Id,
                    RaceId = race.Id,
                    LapCount = 52 + random.Next(0, 5),
                    AverageLapTimeSeconds = decimal.Round(baseline + variation, 3)
                });
            }

            var raceIndex = int.Parse(race.Name["Race ".Length..]);
            var upgradedDriver = drivers[(raceIndex - 1) % drivers.Count];
            carParts.Add(new CarPart
            {
                Name = $"Front Wing Spec {raceIndex}",
                DriverId = upgradedDriver.Id,
                PerformanceDeltaSeconds = decimal.Round(-0.02m * raceIndex, 3),
                AppliedOn = race.Date
            });
        }

        await dbContext.LapSummaries.AddRangeAsync(lapSummaries, cancellationToken);
        await dbContext.CarParts.AddRangeAsync(carParts, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public static async Task<SeedValidationResult> ValidateAsync(
        PitWallDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        var driverCount = await dbContext.Drivers.CountAsync(cancellationToken);
        var raceCount = await dbContext.Races.CountAsync(cancellationToken);
        var lapSummaryCount = await dbContext.LapSummaries.CountAsync(cancellationToken);
        var carPartCount = await dbContext.CarParts.CountAsync(cancellationToken);

        var hasExpectedLapSummaryCount = lapSummaryCount == driverCount * raceCount;
        var hasInvalidLapRanges = await dbContext.LapSummaries
            .AnyAsync(
                x => x.AverageLapTimeSeconds < 85m ||
                     x.AverageLapTimeSeconds > 95m ||
                     x.LapCount < 50 ||
                     x.LapCount > 60,
                cancellationToken);

        var orphanLapSummaries = await dbContext.LapSummaries
            .CountAsync(
                x => !dbContext.Drivers.Any(d => d.Id == x.DriverId) ||
                     !dbContext.Races.Any(r => r.Id == x.RaceId),
                cancellationToken);

        return new SeedValidationResult(
            DriverCount: driverCount,
            RaceCount: raceCount,
            LapSummaryCount: lapSummaryCount,
            CarPartCount: carPartCount,
            HasExpectedLapSummaryCount: hasExpectedLapSummaryCount,
            HasValidLapRanges: !hasInvalidLapRanges,
            HasValidRelationships: orphanLapSummaries == 0);
    }
}
