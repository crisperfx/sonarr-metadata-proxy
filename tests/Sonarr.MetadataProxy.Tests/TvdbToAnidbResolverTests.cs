using Microsoft.Extensions.Logging.Abstractions;
using Sonarr.MetadataProxy.Options;
using Sonarr.MetadataProxy.Reverse;
using Sonarr.MetadataProxy.Tests.Infrastructure;
using Xunit;

namespace Sonarr.MetadataProxy.Tests;

public class TvdbToAnidbResolverTests : IDisposable
{
    private readonly string _dataDir;

    public TvdbToAnidbResolverTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "metadataproxy-resolver", Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public async Task Resolve_UsesDatamapInverse()
    {
        WriteFixtures();
        WriteTitles("2993|1|en|Death Note");

        var resolver = CreateResolver();
        var anidbId = await resolver.ResolveAnidbIdAsync(81356, "Death Note", 2006, CancellationToken.None);

        Assert.Equal(2993, anidbId);
    }

    [Fact]
    public async Task Resolve_FallsBackToTitleMatchWhenNoDatamap()
    {
        WriteFixturesWithoutDatamap();
        WriteTitles("1234|1|en|Some Anime Title");

        var resolver = CreateResolver();
        var anidbId = await resolver.ResolveAnidbIdAsync(99999, "some anime title", null, CancellationToken.None);

        Assert.Equal(1234, anidbId);
    }

    [Fact]
    public async Task Resolve_NoMappingAndNoTitle_ReturnsNull()
    {
        WriteFixturesWithoutDatamap();
        WriteTitles("1234|1|en|Some Anime Title");

        var resolver = CreateResolver();
        var anidbId = await resolver.ResolveAnidbIdAsync(99999, null, null, CancellationToken.None);

        Assert.Null(anidbId);
    }

    [Fact]
    public async Task Resolve_CachesResult()
    {
        WriteFixtures();
        WriteTitles("2993|1|en|Death Note");

        var resolver = CreateResolver();

        var first = await resolver.ResolveAnidbIdAsync(81356, null, null, CancellationToken.None);
        var second = await resolver.ResolveAnidbIdAsync(81356, null, null, CancellationToken.None);

        Assert.Equal(2993, first);
        Assert.Equal(2993, second);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dataDir, true);
        }
        catch
        {
        }
    }

    private TvdbToAnidbResolver CreateResolver()
    {
        var options = new ProxyOptions { AniListDatamapDir = Path.Combine(_dataDir, "datamaps"), DataDir = _dataDir };
        var map = new AniListTvdbMap(options, NullLogger<AniListTvdbMap>.Instance);
        var titles = new Sonarr.MetadataProxy.Services.AnidbTitleList(options, new HttpClient(), NullLogger<Sonarr.MetadataProxy.Services.AnidbTitleList>.Instance);
        return new TvdbToAnidbResolver(map, titles, NullLogger<TvdbToAnidbResolver>.Instance);
    }

    private void WriteFixtures()
    {
        var dir = Path.Combine(_dataDir, "datamaps");
        Directory.CreateDirectory(dir);

        File.WriteAllText(Path.Combine(dir, "anime.json"), """
        [ { "name": "Death Note", "idAL": 1535, "idAniDB": 2993, "idMal": 1535 } ]
        """);

        File.WriteAllText(Path.Combine(dir, "anime-list-full.xml"), """
        <anime-list>
          <anime anidbid="2993" tvdbid="81356" defaulttvdbseason="1" episodeoffset="" lastupdate="1700000000" />
        </anime-list>
        """);
    }

    private void WriteFixturesWithoutDatamap()
    {
        Directory.CreateDirectory(Path.Combine(_dataDir, "datamaps"));
    }

    private void WriteTitles(params string[] lines)
    {
        TestData.WriteTitleDumpFile(_dataDir, lines);
    }
}