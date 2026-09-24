namespace Sonarr.MetadataProxy.Models.AniList;

public sealed class AniListMedia
{
    public int Id { get; init; }
    public int? IdMal { get; init; }
    public string? TitleRomaji { get; init; }
    public string? TitleEnglish { get; init; }
    public string? TitleNative { get; init; }
    public List<string> Synonyms { get; init; } = new();
    public int? Episodes { get; init; }
    public int? DurationMinutes { get; init; }
    public string? Status { get; init; }
    public string? FirstAirDate { get; init; }
    public string? LastAirDate { get; init; }
    public double AverageScore { get; init; }
    public string? Description { get; init; }
    public string? PosterUrl { get; init; }
    public string? BannerUrl { get; init; }
    public List<string> Genres { get; init; } = new();
    public string? CountryOfOrigin { get; init; }
    public string? Studio { get; init; }
    public List<string> AlternativeTitles { get; init; } = new();
    public List<AniListCast> Cast { get; init; } = new();
}

public sealed class AniListCast
{
    public string? Name { get; init; }
    public string? Character { get; init; }
    public string? ImageUrl { get; init; }
}