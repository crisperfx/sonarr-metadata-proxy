using System.Collections.Concurrent;
using Sonarr.MetadataProxy.Contracts.SkyHook;
using Sonarr.MetadataProxy.Mapping;
using Sonarr.MetadataProxy.Models.Mal;
using Sonarr.MetadataProxy.Options;
using Sonarr.MetadataProxy.Providers;
using Sonarr.MetadataProxy.Reverse;
using Sonarr.MetadataProxy.Translation;

namespace Sonarr.MetadataProxy.Services;

/// <summary>
/// MyAnimeList search front-end (Jikan v4). Finds anime by title/alias through MAL,
/// maps the result to a real TheTVDB id (via the bundled Fribb + Anime-Lists datasets)
/// and returns Sonarr-compatible search results. Details are NOT fetched here: once
/// Sonarr asks for a series it does so by TVDB id, which the normal mapping and
/// TMDB/TVDB pipeline handles.
/// </summary>
public sealed class MalSearchService
{
    private readonly IMalApi _api;
    private readonly AniListTvdbMap _map;
    private readonly MalTranslator _translator;
    private readonly MappingStore _mapping;
    private readonly ILogger<MalSearchService> _logger;
    private readonly TimeSpan _cacheTtl;
    private readonly ConcurrentDictionary<string, CachedResult> _cache = new();

    private sealed record CachedResult(DateTimeOffset StoredAt, IReadOnlyList<ShowResource> Shows);

    public MalSearchService(
        IMalApi api,
        AniListTvdbMap map,
        MalTranslator translator,
        MappingStore mapping,
        ProxyOptions options,
        ILogger<MalSearchService> logger)
    {
        _api = api;
        _map = map;
        _translator = translator;
        _mapping = mapping;
        _logger = logger;
        _cacheTtl = TimeSpan.FromMinutes(options.CacheTtlMinutes);
    }

    public bool IsConfigured => _map.HasData;

    public Task<IReadOnlyList<ShowResource>?> SearchAsync(string query, CancellationToken cancellationToken)
    {
        return GetAsync("search:" + query.ToLowerInvariant(), async () =>
        {
            var media = await _api.SearchAsync(query, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Source: MAL. Jikan result count: {Count}.", media.Count);
            return TranslateAll(media);
        }, cancellationToken);
    }

    public Task<IReadOnlyList<ShowResource>?> SearchByMalIdAsync(int malId, CancellationToken cancellationToken)
    {
        return GetAsync("id:" + malId, async () =>
        {
            var media = await _api.GetByIdAsync(malId, cancellationToken).ConfigureAwait(false);
            return media is null ? Array.Empty<ShowResource>() : TranslateAll(new[] { media });
        }, cancellationToken);
    }

    private async Task<IReadOnlyList<ShowResource>?> GetAsync(
        string key,
        Func<Task<IReadOnlyList<ShowResource>>> load,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return null;
        }

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
        catch (MalApiException ex)
        {
            _logger.LogWarning(ex, "MAL (Jikan) search failed for '{Key}'. Falling back to TVDB.", key);
            return null;
        }
    }

    private IReadOnlyList<ShowResource> TranslateAll(IReadOnlyList<MalAnime> media)
    {
        var results = new List<ShowResource>();
        foreach (var item in media)
        {
            var rawTvdbId = _map.TryGetMalTvdbId(item.Id);
            bool hasRealTvdbMapping = rawTvdbId is > 0;

            int tvdbId;
            if (rawTvdbId is not > 0)
            {
                tvdbId = SyntheticIds.SeriesId(item.Id);
                _logger.LogInformation("No TVDB mapping for MAL {Id} ('{Title}'); using synthetic TVDB id {SyntheticTvdbId}.", item.Id, TitleOf(item), tvdbId);
            }
            else
            {
                tvdbId = rawTvdbId.Value;
                _logger.LogInformation("TVDB mapping found for MAL {MalId}: TVDB {TvdbId}.", item.Id, tvdbId);
            }

            // Register MAL ID + TVDB reverse mapping
            _mapping.RegisterMalId(tvdbId, item.Id);
            
            // If we can find AniList ID via static mapping, register it too
            var anilistId = _map.TryGetAniListId(item.Id);
            if (anilistId is > 0)
            {
                _mapping.RegisterIds(tvdbId, malId: item.Id, anilistId: anilistId.Value);
            }

            var show = _translator.ToSearchResult(item, tvdbId);
            if (!hasRealTvdbMapping)
            {
                show.NoTVDBMapping = true;
            }
            results.Add(show);
        }

        var realCount = media.Count(m => _map.TryGetMalTvdbId(m.Id) is > 0);
        var syntheticCount = media.Count(m => _map.TryGetMalTvdbId(m.Id) is not > 0);
        _logger.LogInformation("TranslateAll returning {Count} results ({Real} real mappings, {Synthetic} synthetic).", results.Count, realCount, syntheticCount);
        return results;
    }

    private static string TitleOf(MalAnime media)
    {
        return media.TitleEnglish ?? media.Title ?? media.TitleJapanese ?? "";
    }
}