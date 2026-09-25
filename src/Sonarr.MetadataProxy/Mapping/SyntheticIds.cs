namespace Sonarr.MetadataProxy.Mapping;

public static class SyntheticIds
{
    public const long SeriesSyntheticBase = 1_000_000_000L;
    public const long TvmazeSeriesSyntheticBase = 2_000_000_000L;
    public const int MinEpisodeSynthetic = 180_000_000;

    public static int SeriesId(int tmdbId)
    {
        var value = SeriesSyntheticBase + tmdbId;
        if (value > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(tmdbId), "TMDB id is too large to synthesize a TVDB id");
        }

        return (int)value;
    }

    public static bool IsSyntheticSeries(int tvdbId)
    {
        return tvdbId >= SeriesSyntheticBase;
    }

    public static bool IsTmdbSyntheticSeries(int tvdbId)
    {
        return IsSyntheticSeries(tvdbId) && !IsTvmazeSyntheticSeries(tvdbId);
    }

    public static int? TryDecomposeSeries(int syntheticTvdbId)
    {
        if (!IsTmdbSyntheticSeries(syntheticTvdbId))
        {
            return null;
        }

        var tmdbId = syntheticTvdbId - SeriesSyntheticBase;
        return tmdbId > 0 ? (int)tmdbId : null;
    }

    public static int TvmazeSeriesId(int tvmazeId)
    {
        var value = TvmazeSeriesSyntheticBase + tvmazeId;
        if (value > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(tvmazeId), "TVMaze id is too large to synthesize a TVDB id");
        }

        return (int)value;
    }

    public static bool IsTvmazeSyntheticSeries(int tvdbId)
    {
        return tvdbId >= TvmazeSeriesSyntheticBase;
    }

    public static int? TryDecomposeTvmazeSeries(int syntheticTvdbId)
    {
        if (!IsTvmazeSyntheticSeries(syntheticTvdbId))
        {
            return null;
        }

        var tvmazeId = syntheticTvdbId - TvmazeSeriesSyntheticBase;
        return tvmazeId > 0 ? (int)tvmazeId : null;
    }
}