using Sonarr.MetadataProxy.Models.Mal;

namespace Sonarr.MetadataProxy.Providers;

public interface IMalApi
{
    Task<IReadOnlyList<MalAnime>> SearchAsync(string query, CancellationToken cancellationToken);

    Task<MalAnime?> GetByIdAsync(int malId, CancellationToken cancellationToken);

    Task<MalPictures?> GetPicturesAsync(int malId, CancellationToken cancellationToken);

    Task<MalAnimeDetails?> GetSeriesDetailsAsync(int malId, CancellationToken cancellationToken);

    Task<IReadOnlyList<MalEpisode>> GetEpisodesAsync(int malId, CancellationToken cancellationToken);
}

public sealed class MalPictures
{
    public List<string> Posters { get; init; } = new();
    public List<string> Backgrounds { get; init; } = new();
}

public sealed class MalAnimeDetails
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
    public List<string> Studios { get; init; } = new();
    public string? Type { get; init; }
    public List<MalSeason> Seasons { get; init; } = new();
    public MalExternalIds? ExternalIds { get; init; }
    public List<MalCast> Cast { get; init; } = new();
}

public sealed class MalSeason
{
    public int Number { get; init; }
    public int? EpisodeCount { get; init; }
    public string? AirDate { get; init; }
    public string? PosterUrl { get; init; }
}

public sealed class MalExternalIds
{
    public int? TvdbId { get; init; }
    public string? ImdbId { get; init; }
}

public sealed class MalEpisode
{
    public int Number { get; init; }
    public int? AbsoluteNumber { get; init; }
    public int? SeasonNumber { get; init; }
    public string? Title { get; init; }
    public string? Overview { get; init; }
    public string? AirDate { get; init; }
    public int? RuntimeMinutes { get; init; }
    public string? StillUrl { get; init; }
    public double? Score { get; init; }
    public int? VoteCount { get; init; }
    public string? EpisodeType { get; init; }
}

public sealed class MalCast
{
    public string? Name { get; init; }
    public string? Character { get; init; }
    public string? ImageUrl { get; init; }
}

public sealed class MalApiException : Exception
{
    public MalApiException(string message) : base(message)
    {
    }

    public MalApiException(string message, Exception innerException) : base(message, innerException)
    {
    }
}