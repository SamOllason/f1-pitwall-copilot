using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public sealed class PitWallDbContext(DbContextOptions<PitWallDbContext> options) : DbContext(options)
{
    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<Race> Races => Set<Race>();
    public DbSet<LapSummary> LapSummaries => Set<LapSummary>();
    public DbSet<CarPart> CarParts => Set<CarPart>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Driver>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Team).HasMaxLength(120).IsRequired();
        });

        modelBuilder.Entity<Race>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Circuit).HasMaxLength(120).IsRequired();
        });

        modelBuilder.Entity<LapSummary>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.AverageLapTimeSeconds).HasPrecision(7, 3);
            entity.HasOne(x => x.Driver).WithMany(x => x.LapSummaries).HasForeignKey(x => x.DriverId);
            entity.HasOne(x => x.Race).WithMany(x => x.LapSummaries).HasForeignKey(x => x.RaceId);
        });
    }
}
