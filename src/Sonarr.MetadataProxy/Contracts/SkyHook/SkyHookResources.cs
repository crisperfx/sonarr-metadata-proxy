namespace Sonarr.MetadataProxy.Contracts.SkyHook;

public sealed class RatingResource
{
    public int Count { get; set; }
    public decimal Value { get; set; }
}

public sealed class TimeOfDayResource
{
    public int Hours { get; set; }
    public int Minutes { get; set; }
}

public sealed class ImageResource
{
    public string? CoverType { get; set; }
    public string? Url { get; set; }
}

public sealed class ActorResource
{
    public string? Name { get; set; }
    public string? Character { get; set; }
    public string? Image { get; set; }
}

public sealed class SeasonResource
{
    public int SeasonNumber { get; set; }
    public List<ImageResource> Images { get; set; } = new();
}

public sealed class EpisodeResource
{
    public int TvdbId { get; set; }
    public int? TvdbShowId { get; set; }
    public int SeasonNumber { get; set; }
    public int EpisodeNumber { get; set; }
    public int? AbsoluteEpisodeNumber { get; set; }
    public string? Title { get; set; }
    public string? AirDate { get; set; }
    public DateTime? AirDateUtc { get; set; }
    public int? Runtime { get; set; }
    public string? FinaleType { get; set; }
    public RatingResource? Rating { get; set; }
    public string? Overview { get; set; }
    public string? Image { get; set; }
    public int? AiredAfterSeasonNumber { get; set; }
    public int? AiredBeforeSeasonNumber { get; set; }
    public int? AiredBeforeEpisodeNumber { get; set; }
}

public sealed class AlternativeTitleResource
{
    public string? Title { get; set; }
}

public sealed class ShowResource
{
    public int TvdbId { get; set; }
    public string? Title { get; set; }
    public string? Overview { get; set; }
    public string? Slug { get; set; }
    public string? OriginalCountry { get; set; }
    public string? OriginalLanguage { get; set; }
    public string? FirstAired { get; set; }
    public string? LastAired { get; set; }
    public int? TvRageId { get; set; }
    public int? TvMazeId { get; set; }
    public int? TmdbId { get; set; }
    public string? ImdbId { get; set; }
    public List<int> MalIds { get; set; } = new();
    public List<int> AniListIds { get; set; } = new();
    public DateTime? LastUpdated { get; set; }
    public string? Status { get; set; }
    public int? Runtime { get; set; }
    public TimeOfDayResource? TimeOfDay { get; set; }
    public string? OriginalNetwork { get; set; }
    public string? Network { get; set; }
    public List<string> Genres { get; set; } = new();
    public string? ContentRating { get; set; }
    public RatingResource? Rating { get; set; }
    public List<AlternativeTitleResource> AlternativeTitles { get; set; } = new();
    public List<ActorResource> Actors { get; set; } = new();
    public List<ImageResource> Images { get; set; } = new();
    public List<SeasonResource> Seasons { get; set; } = new();
    public List<EpisodeResource> Episodes { get; set; } = new();
}