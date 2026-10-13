using Sonarr.MetadataProxy.Models.Tvmaze;

namespace Sonarr.MetadataProxy.Providers;

public interface ITvmazeApi
{
    Task<IReadOnlyList<TvmazeShow>> SearchShowsAsync(string query, CancellationToken cancellationToken);

    Task<TvmazeShow?> GetShowAsync(int tvmazeId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TvmazeEpisode>> GetEpisodesAsync(int tvmazeId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TvmazeShowImage>> GetShowImagesAsync(int tvmazeId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TvmazeAlternateList>> GetAlternateListsAsync(int tvmazeId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TvmazeAlternateEpisode>> GetAlternateEpisodesAsync(int alternateListId, CancellationToken cancellationToken);

    Task<TvmazeShow?> FindByImdbAsync(string imdbId, CancellationToken cancellationToken);

    Task<TvmazeShow?> FindByThetvdbAsync(int tvdbId, CancellationToken cancellationToken);
}