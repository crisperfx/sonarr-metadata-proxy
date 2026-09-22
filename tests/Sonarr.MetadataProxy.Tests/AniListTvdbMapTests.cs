using Microsoft.Extensions.Logging.Abstractions;
using Sonarr.MetadataProxy.Options;
using Sonarr.MetadataProxy.Reverse;
using Xunit;

namespace Sonarr.MetadataProxy.Tests;

public class AniListTvdbMapTests : IDisposable
{
    private readonly string _root;

    public AniListTvdbMapTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "metadataproxy-anilist", Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public void FullDataset_ResolvesTvdbIdFromAniListAndMalIds()
    {
        WriteFixtures();
        var map = CreateMap();

        Assert.True(map.HasData);
        Assert.Equal(81356, map.TryGetTvdbId(1535));
        Assert.Equal(81356, map.TryGetMalTvdbId(1535));
        Assert.Equal(1535, map.TryGetAniListId(1535));
        Assert.Equal(81356, map.TryGetAnidbTvdbId(2993));
    }

    [Fact]
    public void MissingFiles_AreNotConfigured()
    {
        var map = CreateMap();

        Assert.False(map.HasData);
        Assert.Null(map.TryGetTvdbId(1535));
    }

    [Fact]
    public void UnknownAniListId_ReturnsNull()
    {
        WriteFixtures();
        var map = CreateMap();

        Assert.Null(map.TryGetTvdbId(999999));
    }

    [Fact]
    public void ApiEntryWithoutMapping_DoesNotProduceTvdbResult()
    {
        WriteFixtures();
        var map = CreateMap();

        Assert.Null(map.TryGetTvdbId(701));
    }

    private AniListTvdbMap CreateMap()
    {
        return new AniListTvdbMap(
            new ProxyOptions { AniListDatamapDir = Path.Combine(_root, "datamaps"), DataDir = _root },
            NullLogger<AniListTvdbMap>.Instance);
    }

    private void WriteFixtures()
    {
        var dir = Path.Combine(_root, "datamaps");
        Directory.CreateDirectory(dir);

        File.WriteAllText(Path.Combine(dir, "anime.json"), """
        [
          { "name": "Death Note", "name_cn": "", "name_jp": "", "idAL": 1535, "idAniDB": 2993, "idMal": 1535 },
          { "name": "Cowboy Bebop", "name_cn": "", "name_jp": "", "idAL": 1, "idAniDB": 849, "idMal": 1 },
          { "name": "No TVDB link here", "name_cn": "", "name_jp": "", "idAL": 701, "idAniDB": 4242, "idMal": 701 }
        ]
        """);

        File.WriteAllText(Path.Combine(dir, "anime-list-full.xml"), """
        <anime-list>
          <anime anidbid="2993" tvdbid="81356" defaulttvdbseason="1" episodeoffset="" lastupdate="1700000000">
            <episode anidbid="2993" tvdbid="81356" tvdbseason="1" tvdbepisode="1" start="0" end="0" />
          </anime>
          <anime anidbid="849" tvdbid="80087" defaulttvdbseason="1" episodeoffset="" lastupdate="1700000000" />
        </anime-list>
        """);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            /* ignore */
        }
    }
}