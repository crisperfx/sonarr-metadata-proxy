using Sonarr.MetadataProxy.Contracts.SkyHook;
using Sonarr.MetadataProxy.Mapping;
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
    private readonly IMetadataProvider? _activeProvider;
    private readonly ILogger<MetadataRequestHandler> _logger;

    public MetadataRequestHandler(
        ProxyOptions options,
        MappingStore mapping,
        ITvdbToTmdbResolver tvdbToTmdb,
        ISkyHookPassthrough passthrough,
        SkyHookTranslator translator,
        AniListSearchService? aniList,
        IMetadataProvider? activeProvider,
        ILogger<MetadataRequestHandler> logger)
    {
        _options = options;
        _mapping = mapping;
        _tvdbToTmdb = tvdbToTmdb;
        _passthrough = passthrough;
        _translator = translator;
        _aniList = aniList;
        _activeProvider = activeProvider;
        _logger = logger;
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
                return await ForwardAniListResultAsync(shows, rawTerm, cancellationToken).ConfigureAwait(false);
            }
        }

        return await SearchAutomaticAsync(term, rawTerm, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IResult> ForwardAniListResultAsync(
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

            _logger.LogInformation("No results from {Source} for '{Term}'.", _options.MetadataSource, rawTerm);
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

        if (_mapping.GetOverride(tvdbId) == MappingStore.SourceSingleSeason)
        {
            resolution = FlattenToSingleSeason(resolution);
        }

        return resolution switch
        {
            ShowResolution.Mapped mapped => Results.Ok(mapped.Show),
            ShowResolution.Passthrough passed => Results.Content(passed.Response.Body, passed.Response.ContentType, null, passed.Response.StatusCode),
            _ => Results.NotFound(new { error = "mapping unavailable" })
        };
    }

    private ShowResolution FlattenToSingleSeason(ShowResolution resolution)
    {
        return resolution switch
        {
            ShowResolution.Mapped mapped => new ShowResolution.Mapped(SingleSeasonTransformer.Flatten(mapped.Show)),
            ShowResolution.Passthrough passed => FlattenPassthrough(passed),
            _ => resolution
        };
    }

    private ShowResolution FlattenPassthrough(ShowResolution.Passthrough passed)
    {
        var flattened = SingleSeasonTransformer.FlattenPassthrough(passed.Response);
        if (flattened is null)
        {
            _logger.LogWarning(
                "Could not flatten passthrough response for single-season override; returning original response.");
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

        if (_activeProvider is null || _activeProvider.Name == "tvdb")
        {
            return await ReduceFallbackAsync(tvdbId, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var metadata = await _activeProvider.GetSeries(tmdbId.Value.ToString(), cancellationToken).ConfigureAwait(false);
            if (!SyntheticIds.IsSyntheticSeries(tvdbId))
            {
                _mapping.RegisterSeries(tvdbId, tmdbId.Value);
            }

            var seasons = await _activeProvider.GetSeasons(tmdbId.Value.ToString(), cancellationToken).ConfigureAwait(false);
            var show = _translator.ToFullSeries(metadata, seasons, tvdbId);
            _logger.LogInformation(
                "TVDB mapping: {TvdbId}. Returning Sonarr-compatible metadata.",
                show.TvdbId);
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