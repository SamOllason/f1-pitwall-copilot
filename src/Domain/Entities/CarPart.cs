namespace Domain.Entities;

public sealed class CarPart
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DriverId { get; set; }
    public decimal PerformanceDeltaSeconds { get; set; }
    public DateOnly AppliedOn { get; set; }
}
