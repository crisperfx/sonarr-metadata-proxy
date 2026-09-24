using System.Linq;
using System.Text.Json.Nodes;
using Sonarr.MetadataProxy.Contracts.SkyHook;
using Sonarr.MetadataProxy.Mapping;
using Sonarr.MetadataProxy.Models.Mal;
using Sonarr.MetadataProxy.Models.Metadata;
using Sonarr.MetadataProxy.Options;
using Sonarr.MetadataProxy.Passthrough;
using Sonarr.MetadataProxy.Providers;
using Sonarr.MetadataProxy.Reverse;
using Sonarr.MetadataProxy.Terms;
using Sonarr.MetadataProxy.Translation;

namespace Sonarr.MetadataProxy.Services;

public sealed class MetadataRequestHandler
{
    private readonly ProxyOptions _options;
    private readonly MappingStore _mapping;
    private readonly ITvdbToTmdbResolver _tvdbToTmdb;
    private readonly ISkyHookPassthrough _passthrough;
    private readonly SkyHookTranslator _translator;
    private readonly AniListSearchService? _aniList;
    private readonly MalSearchService? _mal;
    private readonly IMalApi? _malApi;
    private readonly AniListTvdbMap? _animeMap;
    private readonly IMetadataProvider? _activeProvider;
    private readonly TmdbMetadataProvider? _tmdbProvider;
    private readonly MalMetadataProvider? _malProvider;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MetadataRequestHandler> _logger;

    public MetadataRequestHandler(
        ProxyOptions options,
        MappingStore mapping,
        ITvdbToTmdbResolver tvdbToTmdb,
        ISkyHookPassthrough passthrough,
        SkyHookTranslator translator,
        AniListSearchService? aniList,
        MalSearchService? mal,
        IMalApi? malApi,
        AniListTvdbMap? animeMap,
        IMetadataProvider? activeProvider,
        TmdbMetadataProvider? tmdbProvider,
        MalMetadataProvider? malProvider,
        IServiceProvider serviceProvider,
        ILogger<MetadataRequestHandler> logger)
    {
        _options = options;
        _mapping = mapping;
        _tvdbToTmdb = tvdbToTmdb;
        _passthrough = passthrough;
        _translator = translator;
        _aniList = aniList;
        _mal = mal;
        _malApi = malApi;
        _animeMap = animeMap;
        _activeProvider = activeProvider;
        _tmdbProvider = tmdbProvider;
        _malProvider = malProvider;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    private IMetadataProvider? TmdbOrActive => _tmdbProvider ?? _activeProvider;

    private IMetadataProvider? GetProviderForSource(string? source)
    {
        return source switch
        {
            MappingStore.SourceMal => _malProvider,
            MappingStore.SourceTmdb => _tmdbProvider,
            _ => _activeProvider
        };
    }

    public async Task<IResult> SearchAsync(string rawTerm, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Incoming Sonarr metadata request: series search, term '{Term}'.", rawTerm);
        var term = TermClassifier.Classify(rawTerm);

        if (term.Kind == TermKind.TvdbId)
        {
            var resolution = await ResolveShowAsync(int.Parse(term.Value), cancellationToken).ConfigureAwait(false);
            return resolution switch
            {
                ShowResolution.Mapped mapped => Results.Ok(new[] { mapped.Show }),
                ShowResolution.Passthrough passed => Results.Content(passed.Response.Body, passed.Response.ContentType, null, passed.Response.StatusCode),
                _ => Results.Ok(Array.Empty<ShowResource>())
            };
        }

        if (term.Kind is TermKind.AniListId or TermKind.MalId)
        {
            if (term.Kind == TermKind.MalId && _mal is { IsConfigured: true })
            {
                _logger.LogInformation("MAL search source preferred for MAL id '{Term}' via Jikan.", term.Value);
                var malShows = await _mal.SearchByMalIdAsync(int.Parse(term.Value), cancellationToken).ConfigureAwait(false);
                return await ForwardWithFallbackAsync(malShows, rawTerm, cancellationToken).ConfigureAwait(false);
            }

            if (_aniList is { IsConfigured: true })
            {
                var shows = term.Kind switch
                {
                    TermKind.AniListId => await _aniList.SearchByAniListIdAsync(int.Parse(term.Value), cancellationToken).ConfigureAwait(false),
                    _ => await _aniList.SearchByMalIdAsync(int.Parse(term.Value), cancellationToken).ConfigureAwait(false)
                };

                return await ForwardAniListResultAsync(shows, rawTerm, cancellationToken).ConfigureAwait(false);
            }

            _logger.LogInformation(
                "Provider {Source} does not support '{Prefix}' lookups yet. Falling through to TVDB.",
                _options.MetadataSource,
                term.Value);
            return await ForwardToTvdbSearchAsync(rawTerm, cancellationToken).ConfigureAwait(false);
        }

        if (term.Kind == TermKind.TvdbSearch)
        {
            _logger.LogInformation("Explicit TVDB search (tvdb:) for '{Term}'.", term.Value);
            return await ForwardToTvdbSearchAsync(term.Value, cancellationToken).ConfigureAwait(false);
        }

        if (term.Kind == TermKind.Title)
        {
            var searchSource = _mapping.GetDefaultSearchSource();
            if (searchSource == MappingStore.SourceTvdb)
            {
                _logger.LogInformation(
                    "Search source preference '{SearchSource}' applies to series search '{Term}'.",
                    searchSource,
                    rawTerm);
                return await ForwardToTvdbSearchAsync(rawTerm, cancellationToken).ConfigureAwait(false);
            }

            if (searchSource == MappingStore.SourceAniList)
            {
                if (_aniList is not { IsConfigured: true })
                {
                    _logger.LogInformation(
                        "Search source preference '{SearchSource}' is set but AniList mapping data is unavailable; falling through to TVDB.",
                        searchSource);
                    return await ForwardToTvdbSearchAsync(rawTerm, cancellationToken).ConfigureAwait(false);
                }

                _logger.LogInformation(
                    "Search source preference '{SearchSource}' applies to series search '{Term}'.",
                    searchSource,
                    rawTerm);
                var shows = await _aniList.SearchAsync(rawTerm, cancellationToken).ConfigureAwait(false);
                return await ForwardWithFallbackAsync(shows, rawTerm, cancellationToken).ConfigureAwait(false);
            }

            if (searchSource == MappingStore.SourceMal)
            {
                if (_mal is not { IsConfigured: true })
                {
                    _logger.LogInformation(
                        "Search source preference '{SearchSource}' is set but MAL mapping data is unavailable; falling through to TVDB.",
                        searchSource);
                    return await ForwardToTvdbSearchAsync(rawTerm, cancellationToken).ConfigureAwait(false);
                }

                _logger.LogInformation(
                    "Search source preference '{SearchSource}' applies to series search '{Term}'.",
                    searchSource,
                    rawTerm);
                var malShows = await _mal.SearchAsync(rawTerm, cancellationToken).ConfigureAwait(false);
                return await ForwardWithFallbackAsync(malShows, rawTerm, cancellationToken).ConfigureAwait(false);
            }

            if (searchSource == MappingStore.SourceTmdb)
            {
                var provider = TmdbOrActive;
                if (provider is null || provider.Name == "tvdb")
                {
                    _logger.LogInformation("TMDB search requested but no TMDB provider configured; falling through to TVDB.");
                    return await ForwardToTvdbSearchAsync(rawTerm, cancellationToken).ConfigureAwait(false);
                }

                _logger.LogInformation(
                    "Search source preference '{SearchSource}' applies to series search '{Term}'.",
                    searchSource,
                    rawTerm);
                return await SearchTmdbWithFallbackAsync(rawTerm, cancellationToken).ConfigureAwait(false);
            }
        }

        return await SearchAutomaticAsync(term, rawTerm, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IResult> ForwardWithFallbackAsync(
        IReadOnlyList<Contracts.SkyHook.ShowResource>? shows,
        string rawTerm,
        CancellationToken cancellationToken)
    {
        if (shows is null)
        {
            _logger.LogInformation("AniList search failed or is misconfigured for '{Term}'. Falling through to TVDB.", rawTerm);
            return await ForwardToTvdbSearchAsync(rawTerm, cancellationToken).ConfigureAwait(false);
        }

        if (shows.Count == 0)
        {
            _logger.LogInformation("No TVDB-mappable AniList results for '{Term}'. Falling through to TVDB.", rawTerm);
            return await ForwardToTvdbSearchAsync(rawTerm, cancellationToken).ConfigureAwait(false);
        }

        return Results.Ok(shows);
    }

    private async Task<IResult> SearchTmdbWithFallbackAsync(string rawTerm, CancellationToken cancellationToken)
    {
        var provider = TmdbOrActive;
        if (provider is null || provider.Name == "tvdb")
        {
            return await ForwardToTvdbSearchAsync(rawTerm, cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<SeriesMetadata> results;
        try
        {
            results = await provider.Search(rawTerm, cancellationToken).ConfigureAwait(false);
        }
        catch (NotSupportedException)
        {
            _logger.LogInformation("Provider {Source} does not support this search. Falling through to TVDB.", _options.MetadataSource);
            return await ForwardToTvdbSearchAsync(rawTerm, cancellationToken).ConfigureAwait(false);
        }
        catch (TmdbApiException ex)
        {
            _logger.LogError(ex, "Could not query metadata source {Source}. Falling back.", _options.MetadataSource);
            return await ForwardToTvdbSearchAsync(rawTerm, cancellationToken).ConfigureAwait(false);
        }

        if (results.Count == 0)
        {
            _logger.LogInformation("No results from {Source} for '{Term}'. Falling through to TVDB.", _options.MetadataSource, rawTerm);
            return await ForwardToTvdbSearchAsync(rawTerm, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Source: {Source}. TMDB result count: {Count}.",
            _options.MetadataSource.ToUpperInvariant(),
            results.Count);

        var translated = results.Select(_translator.ToSearchResult).ToList();
        return Results.Ok(translated);
    }

    private async Task<IResult> ForwardAniListResultAsync(
        IReadOnlyList<Contracts.SkyHook.ShowResource>? shows,
        string rawTerm,
        CancellationToken cancellationToken)
    {
        return await ForwardWithFallbackAsync(shows, rawTerm, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IResult> SearchAutomaticAsync(SearchTerm term, string rawTerm, CancellationToken cancellationToken)
    {
        if (_activeProvider is null || _activeProvider.Name == "tvdb")
        {
            return await ForwardToTvdbSearchAsync(rawTerm, cancellationToken).ConfigureAwait(false);
        }

        var explicitTmdbSearch = term.Kind == TermKind.TmdbSearch;

        IReadOnlyList<SeriesMetadata> results;
        try
        {
            results = term.Kind switch
            {
                TermKind.TmdbId => await _activeProvider.SearchById(term.Value, cancellationToken).ConfigureAwait(false),
                TermKind.TmdbSearch => await _activeProvider.Search(term.Value, cancellationToken).ConfigureAwait(false),
                TermKind.ImdbId => await _activeProvider.SearchByImdbId(term.Value, cancellationToken).ConfigureAwait(false),
                _ => await _activeProvider.Search(term.Value, cancellationToken).ConfigureAwait(false)
            };
        }
        catch (NotSupportedException)
        {
            _logger.LogInformation("Provider {Source} does not support this search. Falling through to TVDB.", _options.MetadataSource);
            return await ForwardToTvdbSearchAsync(rawTerm, cancellationToken).ConfigureAwait(false);
        }
        catch (TmdbApiException ex)
        {
            _logger.LogError(ex, "Could not query metadata source {Source}. Falling back.", _options.MetadataSource);
            return await ForwardToTvdbSearchAsync(rawTerm, cancellationToken).ConfigureAwait(false);
        }

        if (results.Count == 0)
        {
            if (explicitTmdbSearch)
            {
                _logger.LogInformation("Explicit TMDB search (tmdb:) returned no results for '{Term}'.", term.Value);
                return Results.Ok(Array.Empty<ShowResource>());
            }

            _logger.LogInformation("No results from {Source} for '{Term}'. Falling through to TVDB.", _options.MetadataSource, rawTerm);
            return await ForwardToTvdbSearchAsync(rawTerm, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Source: {Source}. TMDB result count: {Count}.",
            _options.MetadataSource.ToUpperInvariant(),
            results.Count);

        var translated = results.Select(_translator.ToSearchResult).ToList();
        return Results.Ok(translated);
    }

    public async Task<IResult> ShowAsync(int tvdbId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Incoming Sonarr metadata request: series lookup, TVDB id {TvdbId}.", tvdbId);
        var resolution = await ResolveShowAsync(tvdbId, cancellationToken).ConfigureAwait(false);

        resolution = FlattenIfAnimeBound(tvdbId, resolution);

        // Enrich with MAL pictures if this is a MAL-bound series
        var malId = GetMalId(tvdbId);
        if (malId.HasValue && _malApi is not null)
        {
            resolution = await EnrichWithMalPicturesAsync(resolution, malId.Value, cancellationToken).ConfigureAwait(false);
        }

        return resolution switch
        {
            ShowResolution.Mapped mapped => Results.Ok(mapped.Show),
            ShowResolution.Passthrough passed => Results.Content(passed.Response.Body, passed.Response.ContentType, null, passed.Response.StatusCode),
            _ => Results.NotFound(new { error = "mapping unavailable" })
        };
    }

    private int? GetMalId(int tvdbId)
    {
        var persisted = _mapping.TryGetMalIdByTvdb(tvdbId);
        var staticMap = _animeMap?.TryGetMalIdByTvdb(tvdbId);
        
        if (persisted.HasValue && staticMap.HasValue)
        {
            return Math.Min(persisted.Value, staticMap.Value);
        }
        
        return persisted ?? staticMap;
    }

    private async Task<ShowResolution> EnrichWithMalPicturesAsync(ShowResolution resolution, int malId, CancellationToken cancellationToken)
    {
        try
        {
            var pictures = await _malApi!.GetPicturesAsync(malId, cancellationToken).ConfigureAwait(false);
            if (pictures is null || (pictures.Posters.Count == 0 && pictures.Backgrounds.Count == 0))
            {
                return resolution;
            }

            _logger.LogInformation("Enriching TVDB {TvdbId} with MAL pictures (posters: {Count}, backgrounds: {BgCount}).", malId, pictures.Posters.Count, pictures.Backgrounds.Count);

            return resolution switch
            {
                ShowResolution.Mapped mapped => new ShowResolution.Mapped(InjectMalImages(mapped.Show, pictures)),
                ShowResolution.Passthrough passed => InjectMalImagesPassthrough(passed, pictures),
                _ => resolution
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch MAL pictures for MAL id {MalId}.", malId);
            return resolution;
        }
    }

    private ShowResource InjectMalImages(ShowResource show, MalPictures pictures)
    {
        // Prepend MAL poster as primary poster if available
        if (pictures.Posters.Count > 0)
        {
            show.Images.Insert(0, new ImageResource { CoverType = "poster", Url = pictures.Posters[0] });
        }

        // Add MAL backgrounds as fanart
        foreach (var bg in pictures.Backgrounds.Take(3))
        {
            show.Images.Add(new ImageResource { CoverType = "fanart", Url = bg });
        }

        return show;
    }

    private ShowResolution InjectMalImagesPassthrough(ShowResolution.Passthrough passed, MalPictures pictures)
    {
        JsonNode? body;
        try
        {
            body = JsonNode.Parse(passed.Response.Body);
        }
        catch (Exception)
        {
            return passed;
        }

        if (body is not JsonObject root)
        {
            return passed;
        }

        var imagesArray = root["images"] as JsonArray ?? new JsonArray();
        root["images"] = imagesArray;

        // Add MAL poster as first poster
        if (pictures.Posters.Count > 0)
        {
            imagesArray.Insert(0, new JsonObject
            {
                ["coverType"] = "poster",
                ["url"] = pictures.Posters[0]
            });
        }

        // Add MAL backgrounds
        foreach (var bg in pictures.Backgrounds.Take(3))
        {
            imagesArray.Add(new JsonObject
            {
                ["coverType"] = "fanart",
                ["url"] = bg
            });
        }

        var enrichedResponse = new ProxyResponse(passed.Response.StatusCode, passed.Response.ContentType, root.ToJsonString());
        return new ShowResolution.Passthrough(enrichedResponse);
    }

    private ShowResolution FlattenIfAnimeBound(int tvdbId, ShowResolution resolution)
    {
        var sourceOverride = _mapping.GetOverride(tvdbId);
        var effectiveSource = string.IsNullOrWhiteSpace(sourceOverride) ? _options.MetadataSource : sourceOverride;

        // Only MAL/AniList serve flattened output (no seasons). TVDB and TMDB always
        // keep their real seasons (numbered or named): never flatten those.
        if (effectiveSource is not (MappingStore.SourceMal or MappingStore.SourceAniList))
        {
            return resolution;
        }

        return resolution switch
        {
            ShowResolution.Mapped mapped => new ShowResolution.Mapped(FlattenMapped(mapped.Show)),
            ShowResolution.Passthrough passed => FlattenPassthrough(passed),
            _ => resolution
        };
    }

    private ShowResource FlattenMapped(ShowResource show)
    {
        _logger.LogInformation("Anime-bound series; flattening to a single season.");
        return SingleSeasonTransformer.Flatten(show);
    }

    private ShowResolution FlattenPassthrough(ShowResolution.Passthrough passed)
    {
        var flattened = SingleSeasonTransformer.FlattenPassthrough(passed.Response);
        if (flattened is null)
        {
            _logger.LogWarning(
                "Could not flatten passthrough response for anime-bound series; returning original response.");
            return passed;
        }

        return new ShowResolution.Passthrough(flattened);
    }

    private async Task<ShowResolution> ResolveShowAsync(int tvdbId, CancellationToken cancellationToken)
    {
        var sourceOverride = _mapping.GetOverride(tvdbId);
        if (sourceOverride == MappingStore.SourceTvdb)
        {
            _logger.LogInformation(
                "Source override {Source} active for TVDB id {TvdbId}; passing through to TVDB backend.",
                sourceOverride,
                tvdbId);
            return await ReduceFallbackAsync(tvdbId, cancellationToken).ConfigureAwait(false);
        }

        if (sourceOverride == MappingStore.SourceMal)
        {
            var malId = GetMalId(tvdbId);
            if (malId.HasValue && _malProvider is not null)
            {
                _logger.LogInformation("Source override MAL active for TVDB id {TvdbId}; using MAL provider with MAL id {MalId}.", tvdbId, malId.Value);
                try
                {
                    var metadata = await _malProvider.GetSeriesWithTvdbId(malId.Value.ToString(), tvdbId, cancellationToken).ConfigureAwait(false);
                    var seasons = await _malProvider.GetSeasons(malId.Value.ToString(), cancellationToken).ConfigureAwait(false);
                    var show = _translator.ToFullSeries(metadata, seasons, tvdbId);

                    // Register MAL ID and any static AniList ID
                    var staticAniListId = _mapping.TryGetAniListIdByTvdb(tvdbId);
                    _mapping.RegisterIds(tvdbId, malId: malId.Value, anilistId: staticAniListId);

                    _logger.LogInformation("TVDB mapping: {TvdbId}. Returning Sonarr-compatible metadata via MAL.", show.TvdbId);
                    return new ShowResolution.Mapped(show);
                }
                catch (MalApiException ex)
                {
                    _logger.LogError(ex, "MAL API error for MAL ID {MalId}.", malId.Value);
                }
            }
            _logger.LogWarning("MAL override for TVDB id {TvdbId} but no MAL ID found or provider unavailable. Falling back.", tvdbId);
        }

        int? tmdbId = null;

        if (SyntheticIds.IsSyntheticSeries(tvdbId))
        {
            tmdbId = SyntheticIds.TryDecomposeSeries(tvdbId);
        }
        else
        {
            tmdbId = _mapping.TryResolveSeriesTmdb(tvdbId);
            if (!tmdbId.HasValue)
            {
                if (_mapping.GetDefaultSearchSource() == MappingStore.SourceTvdb)
                {
                    if (_options.EnableTvdbFallback)
                    {
                        _mapping.SetOverride(tvdbId, MappingStore.SourceTvdb);
                    }

                    _logger.LogInformation(
                        "Default search source 'tvdb' applies to TVDB id {TvdbId}; saving TVDB override and serving from TVDB backend instead of reverse-mapping.",
                        tvdbId);
                }
                else
                {
                    tmdbId = await _tvdbToTmdb.ResolveTmdbIdAsync(tvdbId, null, null, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        if (!tmdbId.HasValue)
        {
            if (sourceOverride == MappingStore.SourceTmdb)
            {
                _logger.LogWarning(
                    "Source override tmdb requested for TVDB id {TvdbId} but no TMDB mapping found. Falling back to TVDB passthrough.",
                    tvdbId);
            }
            else
            {
                _logger.LogInformation("Mapping unavailable for TVDB id {TvdbId} in {Source}.", tvdbId, _options.MetadataSource);
            }

            return await ReduceFallbackAsync(tvdbId, cancellationToken).ConfigureAwait(false);
        }

        var provider = GetProviderForSource(sourceOverride) ?? _activeProvider;
        if (provider is null || provider.Name == "tvdb")
        {
            return await ReduceFallbackAsync(tvdbId, cancellationToken).ConfigureAwait(false);
        }

try
            {
                var metadata = await provider.GetSeries(tmdbId.Value.ToString(), cancellationToken).ConfigureAwait(false);
                if (!SyntheticIds.IsSyntheticSeries(tvdbId))
                {
                    _mapping.RegisterSeries(tvdbId, tmdbId.Value);
                }

                // Register MAL/AniList IDs from static mapping if available
                var staticMalId = _animeMap?.TryGetMalIdByTvdb(tvdbId);
                var staticAniListId = _mapping.TryGetAniListIdByTvdb(tvdbId);
                if (staticMalId.HasValue || staticAniListId.HasValue)
                {
                    _mapping.RegisterIds(tvdbId, tmdbId.Value, staticMalId, staticAniListId);
                }

                var seasons = await provider.GetSeasons(tmdbId.Value.ToString(), cancellationToken).ConfigureAwait(false);
                var show = _translator.ToFullSeries(metadata, seasons, tvdbId);
                _logger.LogInformation(
                    "TVDB mapping: {TvdbId}. Returning Sonarr-compatible metadata via {Provider}.",
                    show.TvdbId,
                    provider.Name);
                return new ShowResolution.Mapped(show);
            }
        catch (NotSupportedException)
        {
            _logger.LogInformation("Provider {Source} cannot resolve this series. Falling through to TVDB.", _options.MetadataSource);
            return await ReduceFallbackAsync(tvdbId, cancellationToken).ConfigureAwait(false);
        }
        catch (TmdbApiException ex)
        {
            _logger.LogError(ex, "Could not map TMDB ID {TmdbId} to TVDB ID.", tmdbId.Value);
            return await ReduceFallbackAsync(tvdbId, cancellationToken).ConfigureAwait(false);
        }
        catch (MalApiException ex)
        {
            _logger.LogError(ex, "MAL API error for MAL ID {MalId}.", tmdbId.Value);
            return await ReduceFallbackAsync(tvdbId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<ShowResolution> ReduceFallbackAsync(int tvdbId, CancellationToken cancellationToken)
    {
        if (!_options.EnableTvdbFallback)
        {
            return new ShowResolution.NotFound();
        }

        var response = await _passthrough.ShowAsync(tvdbId, cancellationToken).ConfigureAwait(false);
        return new ShowResolution.Passthrough(response);
    }

    private async Task<IResult> ForwardToTvdbSearchAsync(string rawTerm, CancellationToken cancellationToken)
    {
        if (!_options.EnableTvdbFallback)
        {
            _logger.LogDebug("TVDB fallback disabled. Returning empty search result for '{Term}'.", rawTerm);
            return Results.Ok(Array.Empty<ShowResource>());
        }

        var response = await _passthrough.SearchAsync(rawTerm, cancellationToken).ConfigureAwait(false);
        return Results.Content(response.Body, response.ContentType, null, response.StatusCode);
    }

    private abstract record ShowResolution
    {
        public sealed record Mapped(ShowResource Show) : ShowResolution;

        public sealed record Passthrough(ProxyResponse Response) : ShowResolution;

        public sealed record NotFound : ShowResolution;
    }
}