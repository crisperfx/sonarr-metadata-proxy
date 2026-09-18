using Sonarr.MetadataProxy.Mapping;
using Xunit;

namespace Sonarr.MetadataProxy.Tests;

public class SyntheticIdsTests
{
    [Theory]
    [InlineData(1396, 1000001396)]
    [InlineData(1, 1000000001)]
    [InlineData(260000, 1000260000)]
    public void SeriesId_SynthesizesInDedicatedRange(int tmdbId, int expected)
    {
        Assert.Equal(expected, SyntheticIds.SeriesId(tmdbId));
    }

    [Fact]
    public void SeriesId_IsInvertible()
    {
        var tmdbId = 1399;

        var synthesized = SyntheticIds.SeriesId(tmdbId);
        var decomposed = SyntheticIds.TryDecomposeSeries(synthesized);

        Assert.Equal(tmdbId, decomposed);
    }

    [Theory]
    [InlineData(81189)]
    [InlineData(1)]
    [InlineData(150000000)]
    public void IsSyntheticSeries_ReturnsFalseForRealTvdbRanges(int tvdbId)
    {
        Assert.False(SyntheticIds.IsSyntheticSeries(tvdbId));
        Assert.Null(SyntheticIds.TryDecomposeSeries(tvdbId));
    }

    [Fact]
    public void IsSyntheticSeries_ReturnsTrueForSyntheticRange()
    {
        Assert.True(SyntheticIds.IsSyntheticSeries(SyntheticIds.SeriesId(42)));
    }

    [Fact]
    public void SeriesId_DoesNotCollideWithRealTvdbIds()
    {
        var synthetic = SyntheticIds.SeriesId(1_000_000);

        Assert.True(synthetic > int.MaxValue / 3);
        Assert.False(SyntheticIds.IsSyntheticSeries(4_000_000));
    }
}