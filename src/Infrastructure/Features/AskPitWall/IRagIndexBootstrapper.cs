namespace Infrastructure.Features.AskPitWall;

public interface IRagIndexBootstrapper
{
    Task EnsureIndexedAsync(CancellationToken cancellationToken = default);
}
