using System.Text.Json.Nodes;
using Sonarr.MetadataProxy.Contracts.SkyHook;
using Sonarr.MetadataProxy.Passthrough;

namespace Sonarr.MetadataProxy.Services;

public static class SingleSeasonTransformer
{
    public static ShowResource Flatten(ShowResource show)
    {
        var specials = new List<EpisodeResource>();
        var regular = new List<EpisodeResource>();
        foreach (var episode in show.Episodes)
        {
            if (episode.SeasonNumber <= 0)
            {
                specials.Add(episode);
            }
            else
            {
                regular.Add(episode);
            }
        }

        regular = regular
            .OrderBy(episode => episode.AbsoluteEpisodeNumber ?? int.MaxValue)
            .ThenBy(episode => episode.AirDateUtc ?? DateTime.MaxValue)
            .ThenBy(episode => episode.EpisodeNumber)
            .ToList();

        for (var i = 0; i < regular.Count; i++)
        {
            regular[i].SeasonNumber = 1;
            regular[i].EpisodeNumber = i + 1;
            regular[i].AbsoluteEpisodeNumber = regular[i].AbsoluteEpisodeNumber ?? i + 1;
        }

        show.Seasons = BuildSeasons(show.Seasons, specials.Count > 0, regular.Count > 0);
        show.Episodes = specials.Concat(regular).ToList();
        return show;
    }

    public static ProxyResponse? FlattenPassthrough(ProxyResponse response)
    {
        JsonNode? body;
        try
        {
            body = JsonNode.Parse(response.Body);
        }
        catch (Exception)
        {
            return null;
        }

        if (body is not JsonObject root || root["episodes"] is not JsonArray episodes)
        {
            return null;
        }

        var specials = new List<JsonObject>();
        var regular = new List<JsonObject>();
        foreach (var node in episodes)
        {
            if (node is not JsonObject episode)
            {
                continue;
            }

            var seasonNumber = episode["seasonNumber"]?.GetValue<int>() ?? 0;
            if (seasonNumber <= 0)
            {
                specials.Add(episode);
            }
            else
            {
                regular.Add(episode);
            }
        }

        var indexed = regular
            .Select((episode, index) => (Episode: episode, Index: index))
            .OrderBy(pair => AbsoluteNumber(pair.Episode))
            .ThenBy(pair => pair.Index)
            .Select(pair => pair.Episode)
            .ToList();

        var flat = new JsonArray();
        foreach (var special in specials)
        {
            flat.Add(JsonNode.Parse(special.ToJsonString()));
        }

        for (var i = 0; i < indexed.Count; i++)
        {
            var copy = (JsonObject)JsonNode.Parse(indexed[i].ToJsonString());
            copy["seasonNumber"] = 1;
            copy["episodeNumber"] = i + 1;
            copy["absoluteEpisodeNumber"] = AbsoluteNumber(copy) ?? i + 1;
            flat.Add(copy);
        }

        root["episodes"] = flat;
        return new ProxyResponse(response.StatusCode, response.ContentType, root.ToJsonString());
    }

    private static int? AbsoluteNumber(JsonObject episode)
    {
        if (episode["absoluteEpisodeNumber"] is not JsonValue value || !value.TryGetValue<int>(out var number))
        {
            return null;
        }

        return number;
    }

    private static List<SeasonResource> BuildSeasons(IReadOnlyList<SeasonResource> current, bool hasSpecials, bool hasRegular)
    {
        var result = new List<SeasonResource>();
        if (hasSpecials)
        {
            result.Add(new SeasonResource { SeasonNumber = 0 });
        }

        if (hasRegular)
        {
            var seasonOneImages = current.FirstOrDefault(season => season.SeasonNumber == 1)?.Images ?? new List<ImageResource>();
            result.Add(new SeasonResource { SeasonNumber = 1, Images = seasonOneImages });
        }

        return result;
    }
}