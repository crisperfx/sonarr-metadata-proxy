using System.Collections.Concurrent;
using Sonarr.MetadataProxy.Contracts.SkyHook;
using Sonarr.MetadataProxy.Mapping;
using Sonarr.MetadataProxy.Models.Anidb;
using Sonarr.MetadataProxy.Options;
using Sonarr.MetadataProxy.Providers;
using Sonarr.MetadataProxy.Reverse;
using Sonarr.MetadataProxy.Translation;

namespace Sonarr.MetadataProxy.Services;

public sealed class AnidbSearchService
{
    private readonly IAnidbApi _api;
    private readonly AnidbTitleList _titles;
    private readonly AnidbTranslator _translator;
    private readonly MappingStore _mapping;
    private readonly AniListTvdbMap _map;
    private readonly ProxyOptions _options;
    private readonly ILogger<AnidbSearchService> _logger;
    private readonly TimeSpan _cacheTtl;
    private readonly ConcurrentDictionary<string, CachedResult> _cache = new();
    private readonly ConcurrentDictionary<int, string> _posterCache = new();

    private sealed record CachedResult(DateTimeOffset StoredAt, IReadOnlyList<ShowResource> Shows);

    public AnidbSearchService(
        IAnidbApi api,
        AnidbTitleList titles,
        AnidbTranslator translator,
        MappingStore mapping,
        AniListTvdbMap map,
        ProxyOptions options,
        ILogger<AnidbSearchService> logger)
    {
        _api = api;
        _titles = titles;
        _translator = translator;
        _mapping = mapping;
        _map = map;
        _options = options;
        _logger = logger;
        _cacheTtl = TimeSpan.FromMinutes(options.CacheTtlMinutes);
    }

    public bool IsConfigured => _options.HasAnidbClient;

    public async Task<IReadOnlyList<ShowResource>?> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return null;
        }

        var key = "search:" + query.ToLowerInvariant();
        if (_cache.TryGetValue(key, out var cached) && DateTimeOffset.UtcNow - cached.StoredAt < _cacheTtl)
        {
            return cached.Shows;
        }

        try
        {
            await _titles.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            if (!_titles.HasIndex)
            {
                _logger.LogWarning("AniDB title index unavailable; falling back to TVDB for '{Query}'.", query);
                return null;
            }

            var hits = _titles.Search(query).Take(_options.SearchResultLimit).ToList();
            _logger.LogInformation("Source: ANIDB. Title-dump match count: {Count}.", hits.Count);

            var shows = await TranslateHitsAsync(hits, cancellationToken).ConfigureAwait(false);
            _cache[key] = new CachedResult(DateTimeOffset.UtcNow, shows);
            return shows;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AniDB title search failed for '{Query}'. Falling back to TVDB.", query);
            return null;
        }
    }

    public async Task<IReadOnlyList<ShowResource>?> SearchByAnidbIdAsync(int anidbId, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return null;
        }

        var key = "id:" + anidbId;
        if (_cache.TryGetValue(key, out var cached) && DateTimeOffset.UtcNow - cached.StoredAt < _cacheTtl)
        {
            return cached.Shows;
        }

        try
        {
            var anime = await _api.GetAnimeAsync(anidbId, cancellationToken).ConfigureAwait(false);
            if (anime is null)
            {
                return Array.Empty<ShowResource>();
            }

            var tvdbId = ResolveTvdbId(anime.AnidbId, anime.Title);
            _mapping.RegisterAnidbId(tvdbId, anime.AnidbId);

            var show = _translator.ToSearchResult(anime, tvdbId);
            show.NoTVDBMapping = SyntheticIds.IsSyntheticSeries(tvdbId);

            // Cache poster for future title-dump searches
            if (!string.IsNullOrWhiteSpace(anime.Picture))
            {
                _posterCache[anime.AnidbId] = anime.Picture;
            }

            var shows = new List<ShowResource> { show };
            _cache[key] = new CachedResult(DateTimeOffset.UtcNow, shows);
            return shows;
        }
        catch (AnidbApiException ex)
        {
            _logger.LogWarning(ex, "AniDB lookup failed for aid {AnidbId}. Falling back to TVDB.", anidbId);
            return null;
        }
    }

    private async Task<IReadOnlyList<ShowResource>> TranslateHitsAsync(IReadOnlyList<AnidbTitleHit> hits, CancellationToken cancellationToken)
    {
        var results = new List<ShowResource>();
        var seenTvdbIds = new HashSet<int>();
        foreach (var hit in hits)
        {
            // Filter to TV series only (matches AniDB web search type.tvseries=1)
            if (!_map.IsTvSeries(hit.Aid))
            {
                continue;
            }

            var tvdbId = ResolveTvdbId(hit.Aid, hit.Title);
            if (!seenTvdbIds.Add(tvdbId))
            {
                continue; // skip duplicate TVDB IDs
            }

            _mapping.RegisterAnidbId(tvdbId, hit.Aid);

            var show = _translator.ToSearchResult(hit, tvdbId);
            show.NoTVDBMapping = SyntheticIds.IsSyntheticSeries(tvdbId);

            var anime = await TryFetchAsync(hit.Aid, cancellationToken).ConfigureAwait(false);
            if (anime is not null)
            {
                show = _translator.ToSearchResult(anime, tvdbId);
                show.NoTVDBMapping = SyntheticIds.IsSyntheticSeries(tvdbId);

                if (string.IsNullOrWhiteSpace(anime.Picture))
                {
                    _logger.LogInformation("AniDB {AnidbId} ('{Title}') has no picture in API response.", hit.Aid, anime.Title);
                }
                else
                {
                    _posterCache[hit.Aid] = anime.Picture;
                }
            }
            else if (!_posterCache.TryGetValue(hit.Aid, out _))
            {
                _logger.LogWarning("AniDB {AnidbId} ('{Title}') returned no data during search; result has no poster.", hit.Aid, hit.Title);
            }

            if (show.Images.Count == 0 && _posterCache.TryGetValue(hit.Aid, out var cachedPoster))
            {
                show.Images = new List<ImageResource>
                {
                    new() { CoverType = "poster", Url = PosterUrl(cachedPoster) }
                };
            }

            results.Add(show);
        }

        return results;
    }

    private async Task<AnidbAnime?> TryFetchAsync(int anidbId, CancellationToken cancellationToken)
    {
        try
        {
            return await _api.GetAnimeAsync(anidbId, cancellationToken).ConfigureAwait(false);
        }
        catch (AnidbApiException ex)
        {
            _logger.LogWarning(ex, "Failed to fetch data for AniDB {AnidbId} during search.", anidbId);
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error fetching data for AniDB {AnidbId}.", anidbId);
            return null;
        }
    }

    private static string PosterUrl(string picture)
    {
        return picture.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? picture
            : "https://cdn.anidb.net/images/main/" + picture;
    }

    private int ResolveTvdbId(int anidbId, string? title)
    {
        var realTvdbId = _map.TryGetAnidbTvdbId(anidbId);
        if (realTvdbId is > 0)
        {
            _logger.LogInformation("TVDB mapping found for AniDB {AnidbId}: TVDB {TvdbId}.", anidbId, realTvdbId);
            return realTvdbId.Value;
        }

        var synthetic = SyntheticIds.AnidbSeriesId(anidbId);
        _logger.LogInformation("No TVDB mapping for AniDB {AnidbId} ('{Title}'); using synthetic TVDB id {SyntheticTvdbId}.", anidbId, title, synthetic);
        return synthetic;
    }
}