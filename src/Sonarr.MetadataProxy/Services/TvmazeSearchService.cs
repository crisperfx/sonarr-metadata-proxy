using System.Collections.Concurrent;
using Sonarr.MetadataProxy.Contracts.SkyHook;
using Sonarr.MetadataProxy.Mapping;
using Sonarr.MetadataProxy.Models.Tvmaze;
using Sonarr.MetadataProxy.Options;
using Sonarr.MetadataProxy.Providers;
using Sonarr.MetadataProxy.Translation;

namespace Sonarr.MetadataProxy.Services;

/// <summary>
/// TVMaze search front-end. Finds series by title through TVMaze and maps the
/// result to a TheTVDB id via the TVMaze external ids (falling back to a
/// synthetic id) so Sonarr gets search results it can key on the TVDB id.
/// </summary>
public sealed class TvmazeSearchService
{
    private readonly ITvmazeApi _api;
    private readonly TvmazeTranslator _translator;
    private readonly MappingStore _mapping;
    private readonly ILogger<TvmazeSearchService> _logger;
    private readonly TimeSpan _cacheTtl;
    private readonly ConcurrentDictionary<string, CachedResult> _cache = new();

    private sealed record CachedResult(DateTimeOffset StoredAt, IReadOnlyList<ShowResource> Shows);

    public TvmazeSearchService(
        ITvmazeApi api,
        TvmazeTranslator translator,
        MappingStore mapping,
        ProxyOptions options,
        ILogger<TvmazeSearchService> logger)
    {
        _api = api;
        _translator = translator;
        _mapping = mapping;
        _logger = logger;
        _cacheTtl = TimeSpan.FromMinutes(options.CacheTtlMinutes);
    }

    public bool IsConfigured => true;

    public Task<IReadOnlyList<ShowResource>?> SearchAsync(string query, CancellationToken cancellationToken)
    {
        return GetAsync("search:" + query.ToLowerInvariant(), async () =>
        {
            var shows = await _api.SearchShowsAsync(query, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Source: TVMAZE. TVMaze result count: {Count}.", shows.Count);
            return TranslateAll(shows);
        }, cancellationToken);
    }

    public Task<IReadOnlyList<ShowResource>?> SearchByTvmazeIdAsync(int tvmazeId, CancellationToken cancellationToken)
    {
        return GetAsync("id:" + tvmazeId, async () =>
        {
            var show = await _api.GetShowAsync(tvmazeId, cancellationToken).ConfigureAwait(false);
            return show is null ? Array.Empty<ShowResource>() : TranslateAll(new[] { show });
        }, cancellationToken);
    }

    public Task<IReadOnlyList<ShowResource>?> SearchByImdbIdAsync(string imdbId, CancellationToken cancellationToken)
    {
        return GetAsync("imdb:" + imdbId, async () =>
        {
            var show = await _api.FindByImdbAsync(imdbId, cancellationToken).ConfigureAwait(false);
            return show is null ? Array.Empty<ShowResource>() : TranslateAll(new[] { show });
        }, cancellationToken);
    }

    private async Task<IReadOnlyList<ShowResource>?> GetAsync(
        string key,
        Func<Task<IReadOnlyList<ShowResource>>> load,
        CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(key, out var cached) && DateTimeOffset.UtcNow - cached.StoredAt < _cacheTtl)
        {
            return cached.Shows;
        }

        try
        {
            var shows = await load().ConfigureAwait(false);
            _cache[key] = new CachedResult(DateTimeOffset.UtcNow, shows);
            return shows;
        }
        catch (TvmazeApiException ex)
        {
            _logger.LogWarning(ex, "TVMaze search failed for '{Key}'. Falling back to TVDB.", key);
            return null;
        }
    }

    private IReadOnlyList<ShowResource> TranslateAll(IReadOnlyList<TvmazeShow> shows)
    {
        var results = new List<ShowResource>();
        foreach (var show in shows)
        {
            var rawTvdbId = show.Externals?.TheTvdb;
            bool hasRealTvdbMapping = rawTvdbId is > 0;

            int tvdbId;
            if (!hasRealTvdbMapping)
            {
                tvdbId = SyntheticIds.TvmazeSeriesId(show.Id);
                _logger.LogInformation("No TVDB mapping for TVMaze {TvmazeId} ('{Title}'); using synthetic TVDB id {SyntheticTvdbId}.", show.Id, TitleOf(show), tvdbId);
            }
            else
            {
                tvdbId = rawTvdbId!.Value;
                _logger.LogInformation("TVDB mapping found for TVMaze {TvmazeId}: TVDB {TvdbId}.", show.Id, tvdbId);
            }

            _mapping.RegisterTvmazeId(tvdbId, show.Id);

            var resource = _translator.ToSearchResult(show, tvdbId);
            if (!hasRealTvdbMapping)
            {
                resource.NoTVDBMapping = true;
            }
            results.Add(resource);
        }

        var realCount = shows.Count(show => show.Externals?.TheTvdb is > 0);
        var syntheticCount = results.Count - realCount;
        _logger.LogInformation("TranslateAll returning {Count} results ({Real} real mappings, {Synthetic} synthetic).", results.Count, realCount, syntheticCount);
        return results;
    }

    private static string TitleOf(TvmazeShow show)
    {
        return show.Name ?? "";
    }
}