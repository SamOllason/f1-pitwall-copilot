namespace Domain.Entities;

public sealed class Race
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public string Circuit { get; set; } = string.Empty;

    public ICollection<LapSummary> LapSummaries { get; set; } = new List<LapSummary>();
}
