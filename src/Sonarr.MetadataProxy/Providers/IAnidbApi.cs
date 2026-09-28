using Sonarr.MetadataProxy.Models.Anidb;

namespace Sonarr.MetadataProxy.Providers;

public interface IAnidbApi
{
    Task<AnidbAnime?> GetAnimeAsync(int anidbId, CancellationToken cancellationToken);
}