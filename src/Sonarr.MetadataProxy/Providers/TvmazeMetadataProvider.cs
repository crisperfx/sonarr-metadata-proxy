using Sonarr.MetadataProxy.Models.Metadata;
using Sonarr.MetadataProxy.Models.Tvmaze;
using Sonarr.MetadataProxy.Options;
using Sonarr.MetadataProxy.Translation;

namespace Sonarr.MetadataProxy.Providers;

public sealed class TvmazeMetadataProvider : IMetadataProvider
{
    private readonly ITvmazeApi _api;
    private readonly ProxyOptions _options;
    private readonly TvmazeTranslator _translator;
    private readonly ILogger<TvmazeMetadataProvider> _logger;

    public TvmazeMetadataProvider(
        ITvmazeApi api,
        ProxyOptions options,
        TvmazeTranslator translator,
        ILogger<TvmazeMetadataProvider> logger)
    {
        _api = api;
        _options = options;
        _translator = translator;
        _logger = logger;
    }

    public string Name => "tvmaze";

    public async Task<IReadOnlyList<SeriesMetadata>> Search(string query, CancellationToken cancellationToken)
    {
        var shows = await _api.SearchShowsAsync(query, cancellationToken).ConfigureAwait(false);
        return shows
            .Take(_options.SearchResultLimit)
            .Select(_translator.ToSeries)
            .ToList();
    }

    public async Task<IReadOnlyList<SeriesMetadata>> SearchById(string providerId, CancellationToken cancellationToken)
    {
        var show = await FetchShow(providerId, cancellationToken).ConfigureAwait(false);
        return show is null ? Array.Empty<SeriesMetadata>() : new[] { _translator.ToSeries(show) };
    }

    public async Task<IReadOnlyList<SeriesMetadata>> SearchByImdbId(string imdbId, CancellationToken cancellationToken)
    {
        var show = await _api.FindByImdbAsync(imdbId, cancellationToken).ConfigureAwait(false);
        return show is null ? Array.Empty<SeriesMetadata>() : new[] { _translator.ToSeries(show) };
    }

    public async Task<SeriesMetadata> GetSeries(string providerId, CancellationToken cancellationToken)
    {
        var show = await FetchShow(providerId, cancellationToken).ConfigureAwait(false);
        return show is null
            ? throw new InvalidOperationException($"TVMaze series {providerId} not found.")
            : _translator.ToSeries(show);
    }

    public async Task<IReadOnlyList<SeasonMetadata>> GetSeasons(string providerId, CancellationToken cancellationToken)
    {
        if (!int.TryParse(providerId, out var tvmazeId))
        {
            return Array.Empty<SeasonMetadata>();
        }

        var episodes = await _api.GetEpisodesAsync(tvmazeId, cancellationToken).ConfigureAwait(false);

        return episodes
            .Where(episode => episode.Season >= 0)
            .GroupBy(episode => episode.Season)
            .OrderBy(group => group.Key)
            .Select(group => new SeasonMetadata
            {
                SeasonNumber = group.Key,
                Episodes = group.Select(_translator.ToEpisode).ToList()
            })
            .ToList();
    }

    private async Task<TvmazeShow?> FetchShow(string providerId, CancellationToken cancellationToken)
    {
        if (!int.TryParse(providerId, out var tvmazeId))
        {
            return null;
        }

        return await _api.GetShowAsync(tvmazeId, cancellationToken).ConfigureAwait(false);
    }
}