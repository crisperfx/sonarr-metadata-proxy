using Sonarr.MetadataProxy.Tests.Infrastructure;
using Sonarr.MetadataProxy.Translation;
using Xunit;

namespace Sonarr.MetadataProxy.Tests;

public class AnidbTranslatorTests
{
    private readonly AnidbTranslator _translator = new();

    [Fact]
    public void ToSeries_MapsAnimeToSeriesMetadata()
    {
        var series = _translator.ToSeries(TestData.DeathNoteAnime());

        Assert.Equal(TestData.DeathNoteAnidbId.ToString(), series.ProviderId);
        Assert.Equal("Death Note", series.Title);
        Assert.Equal("2006-10-04", series.FirstAirDate);
        Assert.Equal("2007-06-27", series.LastAirDate);
        Assert.Equal("ended", series.Status);
        Assert.Equal(23, series.RuntimeMinutes);
        Assert.Equal(8.7, series.VoteAverage);
        Assert.Equal("https://cdn.anidb.net/images/main/1", series.PosterPath);
        Assert.Equal(new[] { "Mystery", "Psychological" }, series.Genres);
        var season = Assert.Single(series.Seasons);
        Assert.Equal(1, season.SeasonNumber);
        Assert.Equal(2, season.EpisodeCount);
    }

    [Fact]
    public void ToEpisode_MapsRegularAndSpecial()
    {
        var anime = TestData.DeathNoteAnime();
        var regular = anime.Episodes[0];
        var special = anime.Episodes[2];

        var regularEpisode = _translator.ToEpisode(regular);
        Assert.Equal(1, regularEpisode.EpisodeNumber);
        Assert.Equal(1, regularEpisode.AbsoluteEpisodeNumber);
        Assert.Equal("Rebirth", regularEpisode.Title);
        Assert.Equal("standard", regularEpisode.EpisodeType);

        var specialEpisode = _translator.ToEpisode(special);
        Assert.Equal(1, specialEpisode.EpisodeNumber);
        Assert.Null(specialEpisode.AbsoluteEpisodeNumber);
        Assert.Equal("special", specialEpisode.EpisodeType);
        Assert.Equal("https://cdn.anidb.net/images/main/2010", specialEpisode.ImageUrl);
    }

    [Fact]
    public void ToSearchResult_FromAnime_IncludesPosterAndAnidbId()
    {
        var show = _translator.ToSearchResult(TestData.DeathNoteAnime(), TestData.DeathNoteTvdbId);

        Assert.Equal(TestData.DeathNoteTvdbId, show.TvdbId);
        Assert.Equal(TestData.DeathNoteAnidbId, show.AnidbId);
        var poster = Assert.Single(show.Images);
        Assert.Equal("poster", poster.CoverType);
        Assert.Equal("https://cdn.anidb.net/images/main/1", poster.Url);
    }

    [Fact]
    public void ToSearchResult_FromTitleHit_HasNoImages()
    {
        var hit = new Sonarr.MetadataProxy.Services.AnidbTitleHit(TestData.DeathNoteAnidbId, "Death Note");

        var show = _translator.ToSearchResult(hit, TestData.DeathNoteTvdbId);

        Assert.Equal(TestData.DeathNoteTvdbId, show.TvdbId);
        Assert.Equal(TestData.DeathNoteAnidbId, show.AnidbId);
        Assert.Empty(show.Images);
    }
}