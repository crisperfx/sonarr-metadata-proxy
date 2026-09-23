using System.Collections.Concurrent;
using Sonarr.MetadataProxy.Contracts.SkyHook;
using Sonarr.MetadataProxy.Mapping;
using Sonarr.MetadataProxy.Models.AniList;
using Sonarr.MetadataProxy.Options;
using Sonarr.MetadataProxy.Providers;
using Sonarr.MetadataProxy.Reverse;
using Sonarr.MetadataProxy.Translation;

namespace Sonarr.MetadataProxy.Services;

/// <summary>
/// AniList search front-end. Finds anime by title/alias through AniList, maps the
/// result to a real TheTVDB id (via the bundled Fribb + Anime-Lists datasets) and
/// returns Sonarr-compatible search results. Details are NOT fetched here: once
/// Sonarr asks for a series it does so by TVDB id, which the normal mapping and
/// TMDB/TVDB pipeline handles.
/// </summary>
public sealed class AniListSearchService
{
    private readonly IAniListApi _api;
    private readonly AniListTvdbMap _map;
    private readonly AniListTranslator _translator;
    private readonly MappingStore _mapping;
    private readonly ILogger<AniListSearchService> _logger;
    private readonly TimeSpan _cacheTtl;
    private readonly ConcurrentDictionary<string, CachedResult> _cache = new();

    private sealed record CachedResult(DateTimeOffset StoredAt, IReadOnlyList<ShowResource> Shows);

    public AniListSearchService(
        IAniListApi api,
        AniListTvdbMap map,
        AniListTranslator translator,
        MappingStore mapping,
        ProxyOptions options,
        ILogger<AniListSearchService> logger)
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
            _logger.LogInformation("Source: ANILIST. AniList result count: {Count}.", media.Count);
            return TranslateAll(media);
        }, cancellationToken);
    }

    public Task<IReadOnlyList<ShowResource>?> SearchByAniListIdAsync(int anilistId, CancellationToken cancellationToken)
    {
        return GetAsync("id:" + anilistId, async () =>
        {
            var media = await _api.GetByIdAsync(anilistId, cancellationToken).ConfigureAwait(false);
            return media is null ? Array.Empty<ShowResource>() : TranslateAll(new[] { media });
        }, cancellationToken);
    }

    public Task<IReadOnlyList<ShowResource>?> SearchByMalIdAsync(int malId, CancellationToken cancellationToken)
    {
        return GetAsync("mal:" + malId, async () =>
        {
            var anilistId = _map.TryGetAniListId(malId);
            if (anilistId is not > 0)
            {
                _logger.LogInformation("No AniDB/AniList link known for MAL id {MalId}.", malId);
                return Array.Empty<ShowResource>();
            }

            var media = await _api.GetByIdAsync(anilistId.Value, cancellationToken).ConfigureAwait(false);
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
        catch (AniListApiException ex)
        {
            _logger.LogWarning(ex, "AniList search failed for '{Key}'. Falling back to TVDB.", key);
            return null;
        }
    }

    private IReadOnlyList<ShowResource> TranslateAll(IReadOnlyList<AniListMedia> media)
    {
        var results = new List<ShowResource>();
        foreach (var item in media)
        {
            var rawTvdbId = _map.TryGetTvdbId(item.Id);
            bool hasRealTvdbMapping = rawTvdbId is > 0;

            int tvdbId;
            if (!hasRealTvdbMapping)
            {
                tvdbId = SyntheticIds.SeriesId(item.Id);
                _logger.LogInformation("No TVDB mapping for AniList {Id} ('{Title}'); using synthetic TVDB id {SyntheticTvdbId}.", item.Id, TitleOf(item), tvdbId);
                _mapping.RegisterAniListId(tvdbId, item.Id);
            }
            else
            {
                tvdbId = rawTvdbId.Value;
                _logger.LogInformation("TVDB mapping found for AniList {AniListId}: TVDB {TvdbId}.", item.Id, tvdbId);
            }

            if (!hasRealTvdbMapping)
            {
                _logger.LogInformation("Including AniList {AniListId} ('{Title}') with synthetic TVDB id {SyntheticTvdbId}.", item.Id, TitleOf(item), tvdbId);
            }
            _mapping.RegisterAniListId(tvdbId, item.Id);
            var show = _translator.ToSearchResult(item, tvdbId);
            if (!hasRealTvdbMapping)
            {
                show.NoTVDBMapping = true;
            }
            results.Add(show);
        }

        var realCount = media.Count(m => _map.TryGetTvdbId(m.Id) is > 0);
        var syntheticCount = media.Count(m => _map.TryGetTvdbId(m.Id) is not > 0);
        _logger.LogInformation("TranslateAll returning {Count} results ({Real} real mappings, {Synthetic} synthetic).", results.Count, realCount, syntheticCount);
        return results;
    }

    private static string TitleOf(AniListMedia media)
    {
        return media.TitleEnglish ?? media.TitleRomaji ?? media.TitleNative ?? "";
    }
}