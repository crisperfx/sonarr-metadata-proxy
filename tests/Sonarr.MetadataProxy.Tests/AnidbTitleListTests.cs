using Microsoft.Extensions.Logging.Abstractions;
using Sonarr.MetadataProxy.Options;
using Sonarr.MetadataProxy.Services;
using Sonarr.MetadataProxy.Tests.Infrastructure;
using Xunit;

namespace Sonarr.MetadataProxy.Tests;

public class AnidbTitleListTests : IDisposable
{
    private readonly string _dataDir;

    public AnidbTitleListTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "metadataproxy-anidbtitles", Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public async Task EnsureLoaded_LoadsSeededDump()
    {
        TestData.WriteTitleDumpFile(
            _dataDir,
            "2993|1|en|Death Note",
            "2993|2|ja|DESU NOOTO",
            "2993|4|en|Death Note (2006)",
            "80|1|en|Shin Sekai Yori");

        var list = new AnidbTitleList(
            new ProxyOptions { DataDir = _dataDir },
            new HttpClient(),
            NullLogger<AnidbTitleList>.Instance);

        await list.EnsureLoadedAsync(CancellationToken.None);

        Assert.True(list.HasIndex);
        var hits = list.Search("death note");
        var hit = Assert.Single(hits);
        Assert.Equal(2993, hit.Aid);
        Assert.Equal("Death Note", hit.Title);
    }

    [Fact]
    public async Task Search_MatchingTitle_ReturnsInstantHitsWithoutHttp()
    {
        TestData.WriteTitleDumpFile(_dataDir, "2993|1|en|Death Note");

        var list = new AnidbTitleList(
            new ProxyOptions { DataDir = _dataDir },
            new HttpClient(),
            NullLogger<AnidbTitleList>.Instance);

        await list.EnsureLoadedAsync(CancellationToken.None);
        var hits = list.Search("death");

        Assert.Single(hits);
    }

    [Fact]
    public void ParseLine_HandlesPipesInsideTitle()
    {
        var entry = AnidbTitleList.ParseLine("2993|1|en|Death Note | Special| Edition");

        Assert.NotNull(entry);
        Assert.Equal(2993, entry!.Aid);
        Assert.Equal("Death Note | Special| Edition", entry.Title);
    }

    [Fact]
    public void ParseLine_RejectsMalformedLine()
    {
        Assert.Null(AnidbTitleList.ParseLine("not-a-number|1|en|Foo"));
        Assert.Null(AnidbTitleList.ParseLine("2993|||"));
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
}