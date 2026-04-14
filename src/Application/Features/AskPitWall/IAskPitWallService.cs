namespace Application.Features.AskPitWall;

public interface IAskPitWallService
{
    Task<AskPitWallResponseDto> AskAsync(string question, CancellationToken cancellationToken = default);
}
