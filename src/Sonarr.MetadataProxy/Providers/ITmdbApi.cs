using Sonarr.MetadataProxy.Models.Tmdb;

namespace Sonarr.MetadataProxy.Providers;

public interface ITmdbApi
{
    Task<List<TmdbTvSearchResult>> SearchTvAsync(string query, CancellationToken cancellationToken);
    Task<List<TmdbTvSearchResult>> FindByImdbAsync(string imdbId, CancellationToken cancellationToken);
    Task<List<TmdbTvSearchResult>> FindByTvdbAsync(int tvdbId, CancellationToken cancellationToken);
    Task<TmdbTvDetails> GetTvDetailsAsync(int tmdbId, CancellationToken cancellationToken);
    Task<List<TmdbEpisode>> GetSeasonEpisodesAsync(int tmdbId, int seasonNumber, CancellationToken cancellationToken);
    Task<List<TmdbEpisodeGroupSummary>> GetEpisodeGroupsAsync(int tmdbId, CancellationToken cancellationToken);
    Task<TmdbEpisodeGroup> GetEpisodeGroupAsync(string episodeGroupId, CancellationToken cancellationToken);
}

public sealed class TmdbApiException : Exception
{
    public int StatusCode { get; }

    public TmdbApiException(string message, int statusCode) : base(message)
    {
        StatusCode = statusCode;
    }

    public TmdbApiException(string message, Exception innerException) : base(message, innerException)
    {
        StatusCode = 0;
    }
}