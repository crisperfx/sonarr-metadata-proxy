using System.Text.Json.Nodes;
using Sonarr.MetadataProxy.Contracts.SkyHook;

namespace Sonarr.MetadataProxy.Services;

public static class SingleSeasonDetector
{
    private const int MinRegularEpisodes = 20;
    private const double MinAbsoluteRatio = 0.8;

    public static bool IsContinuous(ShowResource show)
    {
        var episodes = show.Episodes
            .Select(e => (Season: e.SeasonNumber, Absolute: e.AbsoluteEpisodeNumber))
            .ToList();
        return IsContinuous(episodes);
    }

    public static bool IsContinuous(JsonObject root)
    {
        if (root["episodes"] is not JsonArray episodes)
        {
            return false;
        }

        var list = new List<(int Season, int? Absolute)>();
        foreach (var node in episodes)
        {
            if (node is not JsonObject episode)
            {
                continue;
            }

            list.Add((episode["seasonNumber"]?.GetValue<int>() ?? 0, AbsoluteNumber(episode)));
        }

        return IsContinuous(list);
    }

    private static bool IsContinuous(IReadOnlyCollection<(int Season, int? Absolute)> episodes)
    {
        var regular = episodes.Where(e => e.Season >= 1).Select(e => e.Absolute).ToList();
        if (regular.Count < MinRegularEpisodes)
        {
            return false;
        }

        var withAbsolute = regular.Where(a => a.HasValue).Select(a => a!.Value).ToList();
        if (withAbsolute.Count < regular.Count * MinAbsoluteRatio)
        {
            return false;
        }

        if (withAbsolute.Count(a => a == 1) != 1)
        {
            return false;
        }

        var min = withAbsolute.Min();
        var max = withAbsolute.Max();
        return max - min + 1 >= regular.Count * 0.9;
    }

    private static int? AbsoluteNumber(JsonObject episode)
    {
        if (episode["absoluteEpisodeNumber"] is not JsonValue value || !value.TryGetValue<int>(out var number))
        {
            return null;
        }

        return number;
    }
}