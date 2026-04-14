namespace Domain.Entities;

public sealed class LapSummary
{
    public int Id { get; set; }
    public int DriverId { get; set; }
    public int RaceId { get; set; }
    public int LapCount { get; set; }
    public decimal AverageLapTimeSeconds { get; set; }

    public Driver Driver { get; set; } = null!;
    public Race Race { get; set; } = null!;
}
