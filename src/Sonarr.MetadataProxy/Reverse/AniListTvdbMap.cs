using System.Text.Json;
using System.Xml.Linq;
using Sonarr.MetadataProxy.Options;

namespace Sonarr.MetadataProxy.Reverse;

/// <summary>
/// Maps AniList / MyAnimeList / AniDB ids to a real TheTVDB series id, so an
/// AniList search result can be presented to Sonarr (which keys series on the
/// TVDB id). Data sources, both loaded once and kept in memory:
///  1. Fribb's anime-and-manga/lists anime.json        : idAL &lt;-&gt; idAniDB &lt;-&gt; idMal
///  2. Anime-Lists/anime-lists anime-list-full.xml     : AniDB &lt;-&gt; TVDB (series only)
/// </summary>
public sealed class AniListTvdbMap
{
    private readonly Dictionary<int, int> _anidbToTvdb = new();
    private readonly Dictionary<int, int> _anilistToAnidb = new();
    private readonly Dictionary<int, int> _malToAnidb = new();
    private readonly Dictionary<int, int> _malToAniList = new();
    private readonly ILogger<AniListTvdbMap> _logger;

    public AniListTvdbMap(ProxyOptions options, ILogger<AniListTvdbMap> logger)
    {
        _logger = logger;
        Load(options.AniListDatamapDir, options.DataDir);
    }

    public bool HasData { get; private set; }

    public int? TryGetTvdbId(int anilistId)
    {
        return _anilistToAnidb.TryGetValue(anilistId, out var anidbId)
            ? TryGetAnidbTvdbId(anidbId)
            : null;
    }

    public int? TryGetMalTvdbId(int malId)
    {
        return _malToAnidb.TryGetValue(malId, out var anidbId)
            ? TryGetAnidbTvdbId(anidbId)
            : null;
    }

    public int? TryGetAnidbTvdbId(int anidbId)
    {
        return _anidbToTvdb.TryGetValue(anidbId, out var tvdbId) ? tvdbId : null;
    }

    public int? TryGetAniListId(int malId)
    {
        return _malToAniList.TryGetValue(malId, out var anilistId) ? anilistId : null;
    }

    private void Load(string datamapDir, string dataDir)
    {
        var animeListFile = FindFile(datamapDir, dataDir, "anime.json");
        var animeListFullFile = FindFile(datamapDir, dataDir, "anime-list-full.xml");

        if (animeListFile is null || animeListFullFile is null)
        {
            _logger.LogWarning(
                "AniList mapping data missing (expected anime.json and anime-list-full.xml in '{Dir}' or '{DataDir}'). "
                + "AniList search will fall through to TVDB.",
                datamapDir,
                dataDir);
            return;
        }

        try
        {
            LoadAnimeList(animeListFile);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not parse AniList mapping data '{File}'.", animeListFile);
            Clear();
        }

        try
        {
            LoadAnimeListFull(animeListFullFile);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not parse AniDB/TVDB mapping data '{File}'.", animeListFullFile);
            Clear();
        }

        HasData = _anidbToTvdb.Count > 0 && _anilistToAnidb.Count > 0;
        _logger.LogInformation(
            "AniList mapping data loaded: {AniList} anilist ids, {AniDb} anidb ids, {Tvdb} anidb->tvdb links.",
            _anilistToAnidb.Count,
            _anidbToTvdb.Count,
            HasData ? _anidbToTvdb.Count : 0);
    }

    private static string? FindFile(string datamapDir, string dataDir, string fileName)
    {
        var candidates = new[]
        {
            Path.Combine(datamapDir, fileName),
            Path.Combine(dataDir, "datamaps", fileName)
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private void LoadAnimeList(string path)
    {
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);

        foreach (var item in document.RootElement.EnumerateArray())
        {
            ParseAniListEntry(item);
        }
    }

    private void ParseAniListEntry(JsonElement item)
    {
        if (!item.TryGetProperty("idAL", out var idAl) || idAl.ValueKind != JsonValueKind.Number || !idAl.TryGetInt32(out var anilistId) || anilistId <= 0)
        {
            return;
        }

        var anidbId = 0;
        if (item.TryGetProperty("idAniDB", out var idAnidb) && idAnidb.ValueKind == JsonValueKind.Number && idAnidb.TryGetInt32(out anidbId) && anidbId > 0)
        {
            _anilistToAnidb[anilistId] = anidbId;
        }

        if (item.TryGetProperty("idMal", out var idMal) && idMal.ValueKind == JsonValueKind.Number && idMal.TryGetInt32(out var malId) && malId > 0)
        {
            _malToAnidb[malId] = anidbId;
            _malToAniList[malId] = anilistId;
        }
    }

    private void LoadAnimeListFull(string path)
    {
        var root = XDocument.Load(path).Root;
        if (root is null)
        {
            return;
        }

        foreach (var anime in root.Elements("anime"))
        {
            ParseAnimeEntry(anime);
        }
    }

    private void ParseAnimeEntry(XElement anime)
    {
        if (!int.TryParse((string?)anime.Attribute("anidbid"), out var anidbId) || anidbId <= 0)
        {
            return;
        }

        var tvdb = (string?)anime.Attribute("tvdbid");
        if (string.IsNullOrWhiteSpace(tvdb) || !int.TryParse(tvdb, out var tvdbId) || tvdbId <= 0)
        {
            return;
        }

        _anidbToTvdb[anidbId] = tvdbId;
    }

    private void Clear()
    {
        _anidbToTvdb.Clear();
        _anilistToAnidb.Clear();
        _malToAnidb.Clear();
    }
}