using Sonarr.MetadataProxy.Models.Anidb;
using Sonarr.MetadataProxy.Providers;
using Xunit;

namespace Sonarr.MetadataProxy.Tests;

public class AnidbClientTests
{
    [Fact]
    public void Parse_ParsesAnimeAndEpisodes()
    {
        var xml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <anime id="2993" restricted="false">
          <type>TV Series</type>
          <startdate>2006-10-04</startdate>
          <enddate>2007-06-27</enddate>
          <titles><title type="main" xml:lang="x-jat">Death Note</title></titles>
          <description>A student finds a supernatural notebook.</description>
          <rating votes="2183">8.70</rating>
          <picture>1</picture>
          <episodecount>37</episodecount>
          <categories>
            <category id="19" weight="0"><name>Mystery</name></category>
            <category id="28" weight="0"><name>Psychological</name></category>
          </categories>
          <episodes>
            <episode id="2001"><epno type="1">1</epno><length>23</length><airdate>2006-10-04</airdate><title>Rebirth</title><rating>8.50</rating><picture>2001</picture></episode>
            <episode id="2010"><epno type="2">1</epno><length>45</length><airdate>2007-09-21</airdate><title>Directives</title><rating>8.00</rating><picture>2010</picture></episode>
          </episodes>
        </anime>
        """.Trim();

        var anime = AnidbXmlParser.Parse(xml);

        Assert.NotNull(anime);
        Assert.Equal(2993, anime!.AnidbId);
        Assert.Equal("Death Note", anime.Title);
        Assert.Equal("2006-10-04", anime.StartDate);
        Assert.Equal("2007-06-27", anime.EndDate);
        Assert.Equal(8.7, anime.Rating);
        Assert.Equal("1", anime.Picture);
        Assert.Equal(37, anime.EpisodeCount);
        Assert.Contains("Mystery", anime.Genres);
        Assert.Equal(2, anime.Episodes.Count);
        Assert.Equal(1, anime.Episodes[0].Type);
        Assert.Equal("Rebirth", anime.Episodes[0].Title);
        Assert.Equal(2, anime.Episodes[1].Type);
        Assert.Equal(45, anime.Episodes[1].LengthMinutes);
    }

    [Fact]
    public void Parse_ReturnsNullForErrorResponse()
    {
        var xml = "<error>Anime not found</error>";

        Assert.Null(AnidbXmlParser.Parse(xml));
    }

    [Fact]
    public void Parse_ReturnsNullForMalformedXml()
    {
        Assert.Throws<AnidbApiException>(() => AnidbXmlParser.Parse("<anime id=\"1\">"));
    }

    [Fact]
    public void Parse_ReadsIdFromRootAttribute()
    {
        var xml = """
        <anime id="69" restricted="false">
          <type>TV Series</type>
          <episodecount>1184</episodecount>
          <startdate>1999-10-20</startdate>
          <titles>
            <title xml:lang="x-jat" type="main">One Piece</title>
            <title xml:lang="ja" type="synonym">ワンピース</title>
          </titles>
          <picture>440.jpg</picture>
        </anime>
        """.Trim();

        var anime = AnidbXmlParser.Parse(xml);

        Assert.NotNull(anime);
        Assert.Equal(69, anime!.AnidbId);
        Assert.Equal("One Piece", anime.Title);
        Assert.Equal("440.jpg", anime.Picture);
    }

    [Fact]
    public void Parse_RatingAcceptsPermanentChild()
    {
        var xml = """
        <anime id="1">
          <rating><permanent>8.50</permanent></rating>
        </anime>
        """.Trim();

        var anime = AnidbXmlParser.Parse(xml);

        Assert.NotNull(anime);
        Assert.Equal(8.5, anime!.Rating);
    }
}