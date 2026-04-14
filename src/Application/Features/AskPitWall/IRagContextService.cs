namespace Application.Features.AskPitWall;

public interface IRagContextService
{
    Task<IReadOnlyList<RagContextChunkDto>> RetrieveAsync(
        string question,
        int top = 5,
        CancellationToken cancellationToken = default);
}

public sealed record RagContextChunkDto(
    string Id,
    string Content,
    string Source,
    string Driver,
    string Race,
    string Circuit,
    string DocType,
    double Score);
