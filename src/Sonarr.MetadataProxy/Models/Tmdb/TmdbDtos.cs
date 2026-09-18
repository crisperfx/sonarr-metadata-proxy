using System.Text.Json.Serialization;

namespace Sonarr.MetadataProxy.Models.Tmdb;

public sealed class TmdbTvSearchResponse
{
    [JsonPropertyName("results")]
    public List<TmdbTvSearchResult> Results { get; set; } = new();
}

public sealed class TmdbTvSearchResult
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("original_name")]
    public string? OriginalName { get; set; }

    [JsonPropertyName("overview")]
    public string? Overview { get; set; }

    [JsonPropertyName("first_air_date")]
    public string? FirstAirDate { get; set; }

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; }

    [JsonPropertyName("backdrop_path")]
    public string? BackdropPath { get; set; }

    [JsonPropertyName("origin_country")]
    public List<string> OriginCountry { get; set; } = new();

    [JsonPropertyName("original_language")]
    public string? OriginalLanguage { get; set; }

    [JsonPropertyName("vote_average")]
    public double VoteAverage { get; set; }

    [JsonPropertyName("vote_count")]
    public int VoteCount { get; set; }
}

public sealed class TmdbTvDetails
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("original_name")]
    public string? OriginalName { get; set; }

    [JsonPropertyName("overview")]
    public string? Overview { get; set; }

    [JsonPropertyName("first_air_date")]
    public string? FirstAirDate { get; set; }

    [JsonPropertyName("last_air_date")]
    public string? LastAirDate { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("in_production")]
    public bool InProduction { get; set; }

    [JsonPropertyName("episode_run_time")]
    public List<int> EpisodeRunTime { get; set; } = new();

    [JsonPropertyName("number_of_seasons")]
    public int NumberOfSeasons { get; set; }

    [JsonPropertyName("networks")]
    public List<TmdbNetwork> Networks { get; set; } = new();

    [JsonPropertyName("genres")]
    public List<TmdbGenre> Genres { get; set; } = new();

    [JsonPropertyName("origin_country")]
    public List<string> OriginCountry { get; set; } = new();

    [JsonPropertyName("original_language")]
    public string? OriginalLanguage { get; set; }

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; }

    [JsonPropertyName("backdrop_path")]
    public string? BackdropPath { get; set; }

    [JsonPropertyName("vote_average")]
    public double VoteAverage { get; set; }

    [JsonPropertyName("vote_count")]
    public int VoteCount { get; set; }

    [JsonPropertyName("created_by")]
    public List<TmdbPerson> CreatedBy { get; set; } = new();

    [JsonPropertyName("seasons")]
    public List<TmdbSeason> Seasons { get; set; } = new();

    [JsonPropertyName("external_ids")]
    public TmdbExternalIds? ExternalIds { get; set; }

    [JsonPropertyName("credits")]
    public TmdbCredits? Credits { get; set; }

    [JsonPropertyName("content_ratings")]
    public TmdbContentRatings? ContentRatings { get; set; }
}

public sealed class TmdbSeason
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("overview")]
    public string? Overview { get; set; }

    [JsonPropertyName("air_date")]
    public string? AirDate { get; set; }

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; }

    [JsonPropertyName("season_number")]
    public int SeasonNumber { get; set; }

    [JsonPropertyName("episode_count")]
    public int EpisodeCount { get; set; }
}

public sealed class TmdbSeasonResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("season_number")]
    public int SeasonNumber { get; set; }

    [JsonPropertyName("episodes")]
    public List<TmdbEpisode> Episodes { get; set; } = new();
}

public sealed class TmdbEpisode
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("overview")]
    public string? Overview { get; set; }

    [JsonPropertyName("episode_number")]
    public int EpisodeNumber { get; set; }

    [JsonPropertyName("season_number")]
    public int SeasonNumber { get; set; }

    [JsonPropertyName("air_date")]
    public string? AirDate { get; set; }

    [JsonPropertyName("runtime")]
    public int? Runtime { get; set; }

    [JsonPropertyName("still_path")]
    public string? StillPath { get; set; }

    [JsonPropertyName("vote_average")]
    public double VoteAverage { get; set; }

    [JsonPropertyName("vote_count")]
    public int VoteCount { get; set; }

    [JsonPropertyName("episode_type")]
    public string? EpisodeType { get; set; }

    [JsonPropertyName("absolute_number")]
    public int? AbsoluteNumber { get; set; }
}

public sealed class TmdbNetwork
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public sealed class TmdbGenre
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public sealed class TmdbPerson
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("profile_path")]
    public string? ProfilePath { get; set; }
}

public sealed class TmdbCredits
{
    [JsonPropertyName("cast")]
    public List<TmdbCast> Cast { get; set; } = new();
}

public sealed class TmdbCast
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("character")]
    public string? Character { get; set; }

    [JsonPropertyName("profile_path")]
    public string? ProfilePath { get; set; }

    [JsonPropertyName("order")]
    public int Order { get; set; }
}

public sealed class TmdbExternalIds
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("imdb_id")]
    public string? ImdbId { get; set; }

    [JsonPropertyName("tvdb_id")]
    public int? TvdbId { get; set; }
}

public sealed class TmdbContentRatings
{
    [JsonPropertyName("results")]
    public List<TmdbContentRating> Results { get; set; } = new();
}

public sealed class TmdbContentRating
{
    [JsonPropertyName("iso_3166_1")]
    public string? Iso3166_1 { get; set; }

    [JsonPropertyName("rating")]
    public string? Rating { get; set; }
}

public sealed class TmdbFindResponse
{
    [JsonPropertyName("tv_results")]
    public List<TmdbTvSearchResult> TvResults { get; set; } = new();
}