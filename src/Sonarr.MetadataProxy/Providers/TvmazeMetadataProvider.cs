using Sonarr.MetadataProxy.Models.Metadata;
using Sonarr.MetadataProxy.Models.Tvmaze;
using Sonarr.MetadataProxy.Options;
using Sonarr.MetadataProxy.Translation;

namespace Sonarr.MetadataProxy.Providers;

public sealed class TvmazeMetadataProvider : IMetadataProvider, IEpisodeOrderProvider
{
    private const string AlternateOrderPrefix = "alt-";

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
        if (show is null)
        {
            throw new InvalidOperationException($"TVMaze series {providerId} not found.");
        }

        var metadata = _translator.ToSeries(show);
        await AttachSeasonTypes(metadata, cancellationToken).ConfigureAwait(false);
        return metadata;
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

    public async Task<IReadOnlyList<SeasonMetadata>?> GetSeasonsForOrder(string providerId, string seasonType, CancellationToken cancellationToken)
    {
        if (!int.TryParse(providerId, out _) || string.IsNullOrWhiteSpace(seasonType) || seasonType == "official")
        {
            return null;
        }

        if (!TryParseAlternateListId(seasonType, out var listId))
        {
            return null;
        }

        var episodes = await FetchAlternateEpisodes(listId, cancellationToken).ConfigureAwait(false);
        var seasons = BuildOrderSeasons(episodes);
        return seasons.Count > 0 ? seasons : null;
    }

    private async Task AttachSeasonTypes(SeriesMetadata metadata, CancellationToken cancellationToken)
    {
        if (!int.TryParse(metadata.ProviderId, out var tvmazeId))
        {
            return;
        }

        IReadOnlyList<TvmazeAlternateList> lists;
        try
        {
            lists = await _api.GetAlternateListsAsync(tvmazeId, cancellationToken).ConfigureAwait(false);
        }
        catch (TvmazeApiException ex)
        {
            _logger.LogWarning(ex, "Could not fetch TVMaze alternate lists for {TvmazeId}.", tvmazeId);
            return;
        }

        foreach (var list in lists.Where(list => list.Id > 0))
        {
            var episodes = await FetchAlternateEpisodes(list.Id, cancellationToken).ConfigureAwait(false);
            var seasons = BuildOrderSeasons(episodes);
            metadata.SeasonTypes.Add(new SeasonTypeMetadata
            {
                Type = AlternateOrderPrefix + list.Id,
                Name = AlternateOrderName(list),
                SeasonNumbers = seasons.Select(season => season.SeasonNumber).ToList(),
                EpisodeCount = seasons.Sum(season => season.Episodes.Count)
            });
        }
    }

    private async Task<IReadOnlyList<TvmazeAlternateEpisode>> FetchAlternateEpisodes(int listId, CancellationToken cancellationToken)
    {
        try
        {
            return await _api.GetAlternateEpisodesAsync(listId, cancellationToken).ConfigureAwait(false);
        }
        catch (TvmazeApiException ex)
        {
            _logger.LogWarning(ex, "Could not fetch TVMaze alternate list {ListId}.", listId);
            return Array.Empty<TvmazeAlternateEpisode>();
        }
    }

    private static IReadOnlyList<SeasonMetadata> BuildOrderSeasons(IReadOnlyList<TvmazeAlternateEpisode> episodes)
    {
        return episodes
            .Where(episode => episode.Season >= 0)
            .GroupBy(episode => episode.Season)
            .OrderBy(group => group.Key)
            .Select(group => new SeasonMetadata
            {
                SeasonNumber = group.Key,
                Episodes = group
                    .OrderBy(episode => episode.Number ?? int.MaxValue)
                    .Select(episode => new EpisodeMetadata
                    {
                        EpisodeNumber = episode.Number ?? 0,
                        AbsoluteEpisodeNumber = null,
                        SourceEpisodeId = CanonicalEpisodeId(episode),
                        Title = episode.Name,
                        AirDate = TvmazeTranslator.NormalizeDate(episode.AirDate),
                        RuntimeMinutes = episode.Runtime,
                        EpisodeType = episode.Type
                    })
                    .ToList()
            })
            .ToList();
    }

    private static bool TryParseAlternateListId(string seasonType, out int listId)
    {
        listId = 0;
        return seasonType.StartsWith(AlternateOrderPrefix, StringComparison.Ordinal) &&
               int.TryParse(seasonType[AlternateOrderPrefix.Length..], out listId) &&
               listId > 0;
    }

    private static int? CanonicalEpisodeId(TvmazeAlternateEpisode episode)
    {
        var href = episode.Links?.Episodes.FirstOrDefault()?.Href;
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        var segment = href.TrimEnd('/').Split('/').LastOrDefault();
        return int.TryParse(segment, out var id) ? id : null;
    }

    private static string AlternateOrderName(TvmazeAlternateList list)
    {
        if (list.DvdRelease)
        {
            return "DVD Order";
        }

        if (list.VerbatimOrder)
        {
            return "Verbatim Order";
        }

        if (list.StreamingPremiere)
        {
            return "Streaming Order";
        }

        if (list.BroadcastPremiere)
        {
            return "Broadcast Order";
        }

        var slug = list.Url?.TrimEnd('/').Split('/').LastOrDefault();
        if (!string.IsNullOrWhiteSpace(slug))
        {
            return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(slug.Replace('-', ' '));
        }

        return "Alternate Order";
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