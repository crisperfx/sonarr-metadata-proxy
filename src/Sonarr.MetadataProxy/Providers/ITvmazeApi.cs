using Sonarr.MetadataProxy.Models.Tvmaze;

namespace Sonarr.MetadataProxy.Providers;

public interface ITvmazeApi
{
    Task<IReadOnlyList<TvmazeShow>> SearchShowsAsync(string query, CancellationToken cancellationToken);

    Task<TvmazeShow?> GetShowAsync(int tvmazeId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TvmazeEpisode>> GetEpisodesAsync(int tvmazeId, CancellationToken cancellationToken);

    Task<TvmazeShow?> FindByImdbAsync(string imdbId, CancellationToken cancellationToken);

    Task<TvmazeShow?> FindByThetvdbAsync(int tvdbId, CancellationToken cancellationToken);
}