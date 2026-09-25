using Sonarr.MetadataProxy.Models.Anidb;
using Sonarr.MetadataProxy.Models.Metadata;
using Sonarr.MetadataProxy.Translation;

namespace Sonarr.MetadataProxy.Providers;

public sealed class AnidbMetadataProvider : IMetadataProvider
{
    private readonly IAnidbApi _api;
    private readonly AnidbTranslator _translator;
    private readonly ILogger<AnidbMetadataProvider> _logger;

    public AnidbMetadataProvider(
        IAnidbApi api,
        AnidbTranslator translator,
        ILogger<AnidbMetadataProvider> logger)
    {
        _api = api;
        _translator = translator;
        _logger = logger;
    }

    public string Name => "anidb";

    public Task<IReadOnlyList<SeriesMetadata>> Search(string query, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("AniDB has no free-text HTTP search; use the title-dump search service instead.");
    }

    public async Task<IReadOnlyList<SeriesMetadata>> SearchById(string providerId, CancellationToken cancellationToken)
    {
        var anime = await FetchAnime(providerId, cancellationToken).ConfigureAwait(false);
        return anime is null ? Array.Empty<SeriesMetadata>() : new[] { _translator.ToSeries(anime) };
    }

    public Task<IReadOnlyList<SeriesMetadata>> SearchByImdbId(string imdbId, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("AniDB does not support IMDB lookups.");
    }

    public async Task<SeriesMetadata> GetSeries(string providerId, CancellationToken cancellationToken)
    {
        var anime = await FetchAnime(providerId, cancellationToken).ConfigureAwait(false);
        return anime is null
            ? throw new InvalidOperationException($"AniDB series {providerId} not found.")
            : _translator.ToSeries(anime);
    }

    public async Task<IReadOnlyList<SeasonMetadata>> GetSeasons(string providerId, CancellationToken cancellationToken)
    {
        var anime = await FetchAnime(providerId, cancellationToken).ConfigureAwait(false);
        if (anime is null)
        {
            return Array.Empty<SeasonMetadata>();
        }

        var regular = anime.Episodes
            .Where(episode => episode.Type == AnidbTranslator.TypeRegularEpisode)
            .OrderBy(episode => episode.EpisodeNumber)
            .Select(_translator.ToEpisode)
            .ToList();

        var specials = anime.Episodes
            .Where(episode => episode.Type == AnidbTranslator.TypeSpecial)
            .OrderBy(episode => episode.EpisodeNumber)
            .Select(_translator.ToEpisode)
            .ToList();

        var seasons = new List<SeasonMetadata>();
        if (specials.Count > 0)
        {
            seasons.Add(new SeasonMetadata { SeasonNumber = 0, Episodes = specials });
        }

        if (regular.Count > 0)
        {
            seasons.Add(new SeasonMetadata { SeasonNumber = 1, Episodes = regular });
        }

        return seasons;
    }

    private async Task<AnidbAnime?> FetchAnime(string providerId, CancellationToken cancellationToken)
    {
        if (!int.TryParse(providerId, out var anidbId))
        {
            return null;
        }

        return await _api.GetAnimeAsync(anidbId, cancellationToken).ConfigureAwait(false);
    }
}