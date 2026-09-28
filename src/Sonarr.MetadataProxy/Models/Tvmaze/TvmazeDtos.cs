using System.Text.Json.Serialization;

namespace Sonarr.MetadataProxy.Models.Tvmaze;

public sealed class TvmazeSearchHit
{
    [JsonPropertyName("score")]
    public double Score { get; set; }

    [JsonPropertyName("show")]
    public TvmazeShow? Show { get; set; }
}

public sealed class TvmazeShow
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("genres")]
    public List<string> Genres { get; set; } = new();

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("runtime")]
    public int? Runtime { get; set; }

    [JsonPropertyName("premiered")]
    public string? Premiered { get; set; }

    [JsonPropertyName("ended")]
    public string? Ended { get; set; }

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("rating")]
    public TvmazeRating? Rating { get; set; }

    [JsonPropertyName("network")]
    public TvmazeNetwork? Network { get; set; }

    [JsonPropertyName("webChannel")]
    public TvmazeNetwork? WebChannel { get; set; }

    [JsonPropertyName("externals")]
    public TvmazeExternals? Externals { get; set; }

    [JsonPropertyName("image")]
    public TvmazeImage? Image { get; set; }

    [JsonPropertyName("_embedded")]
    public TvmazeEmbedded? Embedded { get; set; }
}

public sealed class TvmazeRating
{
    [JsonPropertyName("average")]
    public double? Average { get; set; }
}

public sealed class TvmazeNetwork
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("country")]
    public TvmazeCountry? Country { get; set; }
}

public sealed class TvmazeCountry
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }
}

public sealed class TvmazeExternals
{
    [JsonPropertyName("imdb")]
    public string? Imdb { get; set; }

    [JsonPropertyName("thetvdb")]
    public int? TheTvdb { get; set; }

    [JsonPropertyName("tvrage")]
    public int? TvRage { get; set; }
}

public sealed class TvmazeImage
{
    [JsonPropertyName("medium")]
    public string? Medium { get; set; }

    [JsonPropertyName("original")]
    public string? Original { get; set; }
}

public sealed class TvmazeEmbedded
{
    [JsonPropertyName("seasons")]
    public List<TvmazeSeason> Seasons { get; set; } = new();

    [JsonPropertyName("cast")]
    public List<TvmazeCast> Cast { get; set; } = new();
}

public sealed class TvmazeSeason
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("number")]
    public int? Number { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("premiereDate")]
    public string? PremiereDate { get; set; }

    [JsonPropertyName("endDate")]
    public string? EndDate { get; set; }

    [JsonPropertyName("episodeOrder")]
    public int? EpisodeOrder { get; set; }

    [JsonPropertyName("image")]
    public TvmazeImage? Image { get; set; }
}

public sealed class TvmazeCast
{
    [JsonPropertyName("person")]
    public TvmazePerson? Person { get; set; }

    [JsonPropertyName("character")]
    public TvmazePerson? Character { get; set; }
}

public sealed class TvmazePerson
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("image")]
    public TvmazeImage? Image { get; set; }
}

public sealed class TvmazeEpisode
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("season")]
    public int Season { get; set; }

    [JsonPropertyName("number")]
    public int? Number { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("airdate")]
    public string? AirDate { get; set; }

    [JsonPropertyName("runtime")]
    public int? Runtime { get; set; }

    [JsonPropertyName("image")]
    public TvmazeImage? Image { get; set; }

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("rating")]
    public TvmazeRating? Rating { get; set; }
}