namespace Sonarr.MetadataProxy.Models.Metadata;

public sealed record ExternalIdSet(int? TvdbId, string? ImdbId, int? TmdbId);

public sealed class SeriesMetadata
{
    public string ProviderId { get; init; } = "";
    public string Title { get; init; } = "";
    public string? Overview { get; init; }
    public string? OriginalTitle { get; init; }
    public string? FirstAirDate { get; init; }
    public string? LastAirDate { get; init; }
    public string? Status { get; init; }
    public int? RuntimeMinutes { get; init; }
    public string? Network { get; init; }
    public string? OriginalCountryCode { get; init; }
    public string? OriginalLanguageCode { get; init; }
    public string? ContentRating { get; init; }
    public double VoteAverage { get; init; }
    public int VoteCount { get; init; }
    public List<string> Genres { get; init; } = new();
    public string? PosterPath { get; init; }
    public string? BackdropPath { get; init; }
    public ExternalIdSet ExternalIds { get; init; } = new(null, null, null);
    public List<SeasonSummaryMetadata> Seasons { get; init; } = new();
    public List<ActorMetadata> Actors { get; init; } = new();
}

public sealed class SeasonSummaryMetadata
{
    public int SeasonNumber { get; init; }
    public int EpisodeCount { get; init; }
    public string? AirDate { get; init; }
    public string? PosterPath { get; init; }
}

public sealed class ActorMetadata
{
    public string? Name { get; init; }
    public string? Character { get; init; }
    public string? ImageUrl { get; init; }
}

public sealed class SeasonMetadata
{
    public int SeasonNumber { get; init; }
    public List<EpisodeMetadata> Episodes { get; init; } = new();
}

public sealed class EpisodeMetadata
{
    public int EpisodeNumber { get; init; }
    public int? AbsoluteEpisodeNumber { get; init; }
    public string? Title { get; init; }
    public string? Overview { get; init; }
    public string? AirDate { get; init; }
    public int? RuntimeMinutes { get; init; }
    public string? ImageUrl { get; init; }
    public double VoteAverage { get; init; }
    public int VoteCount { get; init; }
    public string? EpisodeType { get; init; }
}