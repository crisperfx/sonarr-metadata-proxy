using Sonarr.MetadataProxy.Tests.Infrastructure;
using Sonarr.MetadataProxy.Translation;
using Xunit;

namespace Sonarr.MetadataProxy.Tests;

public class MalTranslatorTests
{
    [Fact]
    public void ToSearchResult_MapsMalAnimeToShowResource()
    {
        var translator = new MalTranslator();

        var show = translator.ToSearchResult(TestData.DeathNoteMal(), 81356);

        Assert.Equal(81356, show.TvdbId);
        Assert.Equal("Death Note", show.Title);
        Assert.Equal("ended", show.Status);
        Assert.Equal("2006-10-04", show.FirstAired);
        Assert.Equal(23, show.Runtime);
        Assert.Equal("Madhouse", show.Network);
        Assert.Equal("JP", show.OriginalCountry);
        Assert.Equal("ja", show.OriginalLanguage);
        Assert.Equal(8.6m, show.Rating.Value);
        Assert.Equal(12345, show.Rating.Count);
        Assert.Equal(new[] { 1535 }, show.MalIds);
        Assert.Contains("Mystery", show.Genres);
        Assert.Contains(show.AlternativeTitles, a => a.Title == "\u30c7\u30b9\u30ce\u30fc\u30c8");
        Assert.Contains(show.Images, i => i.CoverType == "poster");
    }
}