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
    private readonly Dictionary<int, int> _tvdbToMal = new();
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

    public int? TryGetMalIdByTvdb(int tvdbId)
    {
        return _tvdbToMal.TryGetValue(tvdbId, out var malId) ? malId : null;
    }

    private void Load(string datamapDir, string dataDir)
    {
        var animeListFile = FindFile(datamapDir, dataDir, "anime.json");
        var animeListFullFile = FindFile(datamapDir, dataDir, "anime-list-full.xml");

        if (animeListFile is null || animeListFullFile is null)
        {
            _logger.LogWarning(
                "Anime mapping data missing (expected anime.json and anime-list-full.xml in '{Dir}' or '{DataDir}'). "
                + "AniList and MAL lookups will fall through to TVDB.",
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

        var anidbToMal = _malToAnidb
            .GroupBy(kvp => kvp.Value)
            .ToDictionary(g => g.Key, g => g.First().Key);
        foreach (var (anidbId, tvdbId) in _anidbToTvdb)
        {
            if (anidbToMal.TryGetValue(anidbId, out var malId))
            {
                _tvdbToMal[tvdbId] = malId;
            }
        }

        if (_tvdbToMal.TryGetValue(81797, out var onePieceMalId))
        {
            _logger.LogInformation("One Piece (TVDB 81797) reverse MAL mapping: {MalId} (via AniDB {AniDbId}).", onePieceMalId, _anidbToTvdb.FirstOrDefault(kvp => kvp.Value == 81797).Key);
        }
        var anidbOnePiece = _anidbToTvdb.FirstOrDefault(kvp => kvp.Value == 81797).Key;
        if (anidbOnePiece > 0 && _malToAnidb.Any(kvp => kvp.Value == anidbOnePiece))
        {
            var allMal = _malToAnidb.Where(kvp => kvp.Value == anidbOnePiece).Select(kvp => kvp.Key).ToList();
            _logger.LogInformation("AniDB {AniDbId} (One Piece) maps to MAL IDs: {MalIds}.", anidbOnePiece, string.Join(", ", allMal));
        }

        _logger.LogInformation(
            "Anime mapping data loaded (shared AniList/MAL): {AniList} anilist ids, {AniDb} anidb ids, {Tvdb} anidb->tvdb links, {Mal} tvdb->mal links.",
            _anilistToAnidb.Count,
            _anidbToTvdb.Count,
            HasData ? _anidbToTvdb.Count : 0,
            _tvdbToMal.Count);
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