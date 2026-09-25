namespace Sonarr.MetadataProxy.Models.Anidb;

public sealed class AnidbAnime
{
    public int AnidbId { get; set; }
    public string? Title { get; set; }
    public string? Type { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public string? Description { get; set; }
    public double Rating { get; set; }
    public string? Picture { get; set; }
    public int EpisodeCount { get; set; }
    public List<string> Genres { get; set; } = new();
    public List<AnidbEpisode> Episodes { get; set; } = new();
}

public sealed class AnidbEpisode
{
    public int EpisodeId { get; set; }
    public int EpisodeNumber { get; set; }
    public int Type { get; set; }
    public string? Title { get; set; }
    public string? AirDate { get; set; }
    public int? LengthMinutes { get; set; }
    public double Rating { get; set; }
    public string? Picture { get; set; }
}