using Sonarr.MetadataProxy.Mapping;
using Sonarr.MetadataProxy.Tests.Infrastructure;
using Sonarr.MetadataProxy.Translation;
using Xunit;

namespace Sonarr.MetadataProxy.Tests;

public class TvmazeTranslatorTests
{
    private readonly TvmazeTranslator _translator = new();

    [Fact]
    public void ToSeries_MapsShowWithRealTvdbExternal()
    {
        var series = _translator.ToSeries(TestData.BreakingBadTvmaze());

        Assert.Equal("169", series.ProviderId);
        Assert.Equal("Breaking Bad", series.Title);
        Assert.Equal("A chemistry teacher diagnosed with inoperable lung cancer turns to manufacturing methamphetamine.", series.Overview);
        Assert.Equal("2008-01-20", series.FirstAirDate);
        Assert.Equal("2013-09-29", series.LastAirDate);
        Assert.Equal("ended", series.Status);
        Assert.Equal(60, series.RuntimeMinutes);
        Assert.Equal("AMC", series.Network);
        Assert.Equal("US", series.OriginalCountryCode);
        Assert.Equal("en", series.OriginalLanguageCode);
        Assert.Equal(8.6, series.VoteAverage);
        Assert.Equal(3, series.Genres.Count);
        Assert.Equal(TestData.BreakingBadTvdbId, series.ExternalIds.TvdbId);
        Assert.Equal("tt0903747", series.ExternalIds.ImdbId);
        Assert.Null(series.ExternalIds.TmdbId);
        Assert.Single(series.Seasons);
        Assert.Equal(2, series.Actors.Count);
        Assert.Equal("Bryan Cranston", series.Actors[0].Name);
        Assert.Equal("Walter White", series.Actors[0].Character);
    }

    [Fact]
    public void ToSeries_WithoutRealTvdbExternal_UsesSyntheticTvdbId()
    {
        var series = _translator.ToSeries(TestData.BlackMirrorNoTvdbExternal());

        Assert.Equal(SyntheticIds.TvmazeSeriesId(96), series.ExternalIds.TvdbId);
        Assert.Equal("en", series.OriginalLanguageCode);
    }

    [Fact]
    public void ToSeries_StripsHtmlFromSummary()
    {
        var show = TestData.BreakingBadTvmaze();

        var series = _translator.ToSeries(show);

        Assert.DoesNotContain("<p>", series.Overview!);
        Assert.DoesNotContain("</p>", series.Overview!);
    }

    [Fact]
    public void ToSeries_NormalizesMissingDateToNull()
    {
        var show = TestData.BreakingBadTvmaze();
        show.Premiered = "not-a-date";

        var series = _translator.ToSeries(show);

        Assert.Null(series.FirstAirDate);
    }

    [Fact]
    public void ToEpisode_MapsEpisodeFields()
    {
        var episode = _translator.ToEpisode(TestData.BreakingBadTvmazeEpisodes()[0]);

        Assert.Equal(1, episode.EpisodeNumber);
        Assert.Equal("Pilot", episode.Title);
        Assert.Equal("2008-01-20", episode.AirDate);
        Assert.Equal(60, episode.RuntimeMinutes);
        Assert.Equal("High school chemistry teacher Walter White is diagnosed with terminal cancer.", episode.Overview);
        Assert.Equal("regular", episode.EpisodeType);
        Assert.Equal("/pilot_original.jpg", episode.ImageUrl);
    }

    [Fact]
    public void ToSearchResult_MapsShowResource()
    {
        var resource = _translator.ToSearchResult(TestData.BreakingBadTvmaze(), TestData.BreakingBadTvdbId);

        Assert.Equal(TestData.BreakingBadTvdbId, resource.TvdbId);
        Assert.Equal("Breaking Bad", resource.Title);
        Assert.Equal("breaking-bad", resource.Slug);
        Assert.Equal("US", resource.OriginalCountry);
        Assert.Equal("en", resource.OriginalLanguage);
        Assert.Equal("2008-01-20", resource.FirstAired);
        Assert.Equal("2013-09-29", resource.LastAired);
        Assert.Equal(TestData.BreakingBadTvmazeId, resource.TvMazeId);
        Assert.Equal("tt0903747", resource.ImdbId);
        Assert.Equal("ended", resource.Status);
        Assert.Equal(60, resource.Runtime);
        Assert.Equal("AMC", resource.Network);
        Assert.Equal(3, resource.Genres.Count);
        Assert.Equal(8.6m, resource.Rating!.Value);

        var poster = Assert.Single(resource.Images);
        Assert.Equal("poster", poster.CoverType);
        Assert.Equal("https://static.tvmaze.com/uploads/images/original_untouched/0/2400.jpg", poster.Url);
    }

    [Fact]
    public void ToSearchResult_WithoutImage_HasNoImages()
    {
        var show = TestData.BlackMirrorNoTvdbExternal();
        show.Image = null;

        var resource = new TvmazeTranslator().ToSearchResult(show, 2000000096);

        Assert.Empty(resource.Images);
    }

    [Theory]
    [InlineData("Running", "continuing")]
    [InlineData("In Production", "continuing")]
    [InlineData("Ended", "ended")]
    [InlineData("Canceled", "ended")]
    [InlineData("To Be Determined", "upcoming")]
    [InlineData("Planned", "upcoming")]
    [InlineData(null, null)]
    [InlineData("Something Else", null)]
    public void MapStatus_MapsTvmazeStatuses(string? status, string? expected)
    {
        Assert.Equal(expected, TvmazeTranslator.MapStatus(status));
    }

    [Theory]
    [InlineData("English", "en")]
    [InlineData("Japanese", "ja")]
    [InlineData("Dutch", "nl")]
    [InlineData("Flemish", "nl")]
    [InlineData(null, null)]
    [InlineData("Klingon", null)]
    public void MapLanguage_MapsToIsoCodes(string? language, string? expected)
    {
        Assert.Equal(expected, TvmazeTranslator.MapLanguage(language));
    }
}