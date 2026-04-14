namespace Domain.Entities;

public sealed class Driver
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Team { get; set; } = string.Empty;

    public ICollection<LapSummary> LapSummaries { get; set; } = new List<LapSummary>();
}
