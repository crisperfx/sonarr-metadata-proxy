using Sonarr.MetadataProxy.Models.AniList;
using Sonarr.MetadataProxy.Models.Tmdb;
using Sonarr.MetadataProxy.Passthrough;
using Sonarr.MetadataProxy.Providers;
using Sonarr.MetadataProxy.Reverse;

namespace Sonarr.MetadataProxy.Tests.Infrastructure;

public sealed class FakeAniListApi : IAniListApi
{
    public List<AniListMedia> SearchResults { get; set; } = new();
    public Dictionary<int, AniListMedia> ById { get; set; } = new();
    public Exception? Exception { get; set; }
    public int SearchCallCount { get; private set; }

    public Task<IReadOnlyList<AniListMedia>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        ThrowIf();
        SearchCallCount++;
        return Task.FromResult<IReadOnlyList<AniListMedia>>(SearchResults);
    }

    public Task<AniListMedia?> GetByIdAsync(int anilistId, CancellationToken cancellationToken)
    {
        ThrowIf();
        return Task.FromResult(ById.TryGetValue(anilistId, out var media) ? media : null);
    }

    private void ThrowIf()
    {
        if (Exception is not null)
        {
            throw Exception;
        }
    }
}

public sealed class FakeTmdbApi : ITmdbApi
{
    public List<TmdbTvSearchResult> SearchResults { get; set; } = new();
    public TmdbTvDetails Details { get; set; } = new();
    public Dictionary<int, TmdbTvDetails> DetailsById { get; set; } = new();
    public Dictionary<int, List<TmdbEpisode>> Seasons { get; set; } = new();
    public Exception? Exception { get; set; }
    public int DetailsCallCount { get; private set; }
    public int SearchCallCount { get; private set; }

    public Task<List<TmdbTvSearchResult>> SearchTvAsync(string query, CancellationToken cancellationToken)
    {
        ThrowIf();
        SearchCallCount++;
        return Task.FromResult(SearchResults);
    }

    public Task<List<TmdbTvSearchResult>> FindByImdbAsync(string imdbId, CancellationToken cancellationToken)
    {
        ThrowIf();
        return Task.FromResult(SearchResults);
    }

    public Task<List<TmdbTvSearchResult>> FindByTvdbAsync(int tvdbId, CancellationToken cancellationToken)
    {
        ThrowIf();
        return Task.FromResult(SearchResults);
    }

    public Task<TmdbTvDetails> GetTvDetailsAsync(int tmdbId, CancellationToken cancellationToken)
    {
        ThrowIf();
        DetailsCallCount++;
        return Task.FromResult(DetailsById.TryGetValue(tmdbId, out var details) ? details : Details);
    }

    public Task<List<TmdbEpisode>> GetSeasonEpisodesAsync(int tmdbId, int seasonNumber, CancellationToken cancellationToken)
    {
        ThrowIf();
        return Task.FromResult(Seasons.TryGetValue(seasonNumber, out var episodes) ? episodes : new List<TmdbEpisode>());
    }

    private void ThrowIf()
    {
        if (Exception is not null)
        {
            throw Exception;
        }
    }
}

public sealed class FakeTvdbResolver : ITvdbToTmdbResolver
{
    public Dictionary<int, int> Map { get; set; } = new();
    public int CallCount { get; private set; }

    public Task<int?> ResolveTmdbIdAsync(int tvdbId, string? title, int? year, CancellationToken cancellationToken)
    {
        CallCount++;
        return Task.FromResult(Map.TryGetValue(tvdbId, out var tmdbId) ? tmdbId : (int?)null);
    }
}

public sealed class FakeSkyHookPassthrough : ISkyHookPassthrough
{
    public ProxyResponse SearchResponse { get; set; } = new(200, "application/json", "[]");
    public ProxyResponse ShowResponse { get; set; } = new(200, "application/json", "{\"tvdbId\":81189,\"title\":\"fallback\"}");
    public string? LastSearchTerm { get; private set; }
    public int ShowCallCount { get; private set; }

    public Task<ProxyResponse> SearchAsync(string rawTerm, CancellationToken cancellationToken)
    {
        LastSearchTerm = rawTerm;
        return Task.FromResult(SearchResponse);
    }

    public Task<ProxyResponse> ShowAsync(int tvdbId, CancellationToken cancellationToken)
    {
        ShowCallCount++;
        return Task.FromResult(ShowResponse);
    }
}