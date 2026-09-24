namespace Sonarr.MetadataProxy.Models.Mal;

public sealed class MalAnime
{
    public int Id { get; init; }
    public string? Title { get; init; }
    public string? TitleEnglish { get; init; }
    public string? TitleJapanese { get; init; }
    public List<string> Synonyms { get; init; } = new();
    public int? Episodes { get; init; }
    public int? DurationMinutes { get; init; }
    public string? Status { get; init; }
    public string? FirstAirDate { get; init; }
    public string? LastAirDate { get; init; }
    public double? Score { get; init; }
    public int? ScoreCount { get; init; }
    public string? Synopsis { get; init; }
    public string? PosterUrl { get; init; }
    public List<string> Genres { get; init; } = new();
    public string? Studio { get; init; }
    public string? Type { get; init; }
}