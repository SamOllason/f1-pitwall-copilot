namespace Infrastructure.Features.AskPitWall;

public sealed record AzureSearchSettings(
    string? Endpoint,
    string? ApiKey,
    string IndexName);
