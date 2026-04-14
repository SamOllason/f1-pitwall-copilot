namespace Infrastructure.Features.AskPitWall;

public sealed record OpenAiSettings(
    string? ApiKey,
    string Model,
    string? Endpoint,
    string EmbeddingModel);
