using Sonarr.MetadataProxy.Models.AniList;
using Sonarr.MetadataProxy.Models.Mal;
using Sonarr.MetadataProxy.Models.Tmdb;
using Sonarr.MetadataProxy.Models.Tvmaze;

namespace Sonarr.MetadataProxy.Tests.Infrastructure;

public static class TestData
{
    public const int BreakingBadTmdbId = 1396;
    public const int BreakingBadTvdbId = 81189;
    public const int BreakingBadTvmazeId = 169;

    public static TmdbTvSearchResult BreakingBadSearchResult()
    {
        return new TmdbTvSearchResult
        {
            Id = BreakingBadTmdbId,
            Name = "Breaking Bad",
            OriginalName = "Breaking Bad",
            Overview = "When Walter White, a New Mexico chemistry teacher, is diagnosed with terminal cancer.",
            FirstAirDate = "2008-01-20",
            PosterPath = "/ggFHVNu6YYI5L9pCfOacjizRGt.jpg",
            OriginCountry = { "US" },
            OriginalLanguage = "en",
            VoteAverage = 8.9,
            VoteCount = 12345
        };
    }

    public static TmdbTvDetails BreakingBadDetails()
    {
        return new TmdbTvDetails
        {
            Id = BreakingBadTmdbId,
            Name = "Breaking Bad",
            OriginalName = "Breaking Bad",
            Overview = "When Walter White, a New Mexico chemistry teacher, is diagnosed with terminal cancer.",
            FirstAirDate = "2008-01-20",
            LastAirDate = "2013-09-29",
            Status = "Ended",
            EpisodeRunTime = { 47 },
            Networks = { new TmdbNetwork { Id = 1, Name = "AMC" } },
            Genres =
            {
                new TmdbGenre { Id = 18, Name = "Drama" },
                new TmdbGenre { Id = 80, Name = "Crime" }
            },
            OriginCountry = { "US" },
            OriginalLanguage = "en",
            PosterPath = "/ggFHVNu6YYI5L9pCfOacjizRGt.jpg",
            BackdropPath = "/bsNm9z2TJfe0WO3RedPGWQ8mG1X.jpg",
            VoteAverage = 8.9,
            VoteCount = 12345,
            ExternalIds = new TmdbExternalIds
            {
                Id = BreakingBadTmdbId,
                ImdbId = "tt0903747",
                TvdbId = BreakingBadTvdbId
            },
            Credits = new TmdbCredits
            {
                Cast =
                {
                    new TmdbCast { Name = "Bryan Cranston", Character = "Walter White", ProfilePath = "/cb.jpeg", Order = 0 },
                    new TmdbCast { Name = "Aaron Paul", Character = "Jesse Pinkman", ProfilePath = "/ap.jpeg", Order = 1 }
                }
            },
            ContentRatings = new TmdbContentRatings
            {
                Results = { new TmdbContentRating { Iso3166_1 = "US", Rating = "TV-MA" } }
            },
            Seasons =
            {
                new TmdbSeason { SeasonNumber = 1, EpisodeCount = 2, PosterPath = "/s1.jpg" },
                new TmdbSeason { SeasonNumber = 2, EpisodeCount = 1, PosterPath = "/s2.jpg" }
            }
        };
    }

    public static List<TmdbEpisode> SeasonOneEpisodes()
    {
        return new List<TmdbEpisode>
        {
            new()
            {
                Id = 62085,
                Name = "Pilot",
                EpisodeNumber = 1,
                SeasonNumber = 1,
                Overview = "A high school chemistry teacher diagnosed with terminal cancer.",
                AirDate = "2008-01-20",
                Runtime = 58,
                StillPath = "/st1.jpg",
                VoteAverage = 8.3,
                VoteCount = 200,
                EpisodeType = "standard"
            },
            new()
            {
                Id = 62086,
                Name = "Cat's in the Bag...",
                EpisodeNumber = 2,
                SeasonNumber = 1,
                Overview = "Walt and Jesse try to dispose of a body.",
                AirDate = "2008-01-27",
                Runtime = 48,
                StillPath = "/st2.jpg",
                VoteAverage = 8.4,
                VoteCount = 150,
                EpisodeType = "standard"
            }
        };
    }

    public static List<TmdbEpisode> SeasonTwoEpisodes()
    {
        return new List<TmdbEpisode>
        {
            new()
            {
                Id = 62101,
                Name = "Seven Thirty-Seven",
                EpisodeNumber = 1,
                SeasonNumber = 2,
                Overview = "Walt and Jesse must deal with the aftermath.",
                AirDate = "2009-03-08",
                Runtime = 47,
                StillPath = "/st3.jpg",
                VoteAverage = 8.5,
                VoteCount = 120,
                EpisodeType = "standard"
            }
        };
    }

    public static AniListMedia DeathNote()
    {
        return new AniListMedia
        {
            Id = 1535,
            IdMal = 1535,
            TitleRomaji = "Death Note",
            TitleEnglish = "Death Note",
            TitleNative = "\u30c7\u30b9\u30ce\u30fc\u30c8",
            Synonyms = new List<string> { "Death Note (2006)" },
            Episodes = 37,
            DurationMinutes = 23,
            Status = "FINISHED",
            FirstAirDate = "2006-10-04",
            LastAirDate = "2007-06-27",
            AverageScore = 77,
            Description = "A high school student finds a notebook that kills anyone whose name is written in it.",
            PosterUrl = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/large/nx1535-1.jpg",
            BannerUrl = "https://s4.anilist.co/file/anilistcdn/media/anime/banner/1535-1.jpg",
            Genres = new List<string> { "Mystery", "Psychological" },
            CountryOfOrigin = "JP",
            Studio = "Madhouse",
            AlternativeTitles = new List<string> { "Death Note (2006)" }
        };
    }

    public static MalAnime DeathNoteMal()
    {
        return new MalAnime
        {
            Id = 1535,
            Title = "Death Note",
            TitleEnglish = "Death Note",
            TitleJapanese = "\u30c7\u30b9\u30ce\u30fc\u30c8",
            Synonyms = new List<string> { "Death Note (2006)" },
            Episodes = 37,
            DurationMinutes = 23,
            Status = "Finished Airing",
            FirstAirDate = "2006-10-04",
            LastAirDate = "2007-06-27",
            Score = 8.6,
            ScoreCount = 12345,
            Synopsis = "A high school student finds a notebook that kills anyone whose name is written in it.",
            PosterUrl = "https://cdn.myanimelist.net/images/anime/9/9453l.jpg",
            Genres = new List<string> { "Mystery", "Psychological" },
            Studio = "Madhouse",
            Type = "TV"
        };
    }

    public static TvmazeShow BreakingBadTvmaze()
    {
        return new TvmazeShow
        {
            Id = BreakingBadTvmazeId,
            Name = "Breaking Bad",
            Language = "English",
            Genres = new List<string> { "Drama", "Crime", "Thriller" },
            Status = "Ended",
            Runtime = 60,
            Premiered = "2008-01-20",
            Ended = "2013-09-29",
            Summary = "<p>A chemistry teacher diagnosed with inoperable lung cancer turns to manufacturing methamphetamine.</p>",
            Rating = new TvmazeRating { Average = 8.6 },
            Network = new TvmazeNetwork { Id = 1, Name = "AMC", Country = new TvmazeCountry { Name = "United States", Code = "US" } },
            Externals = new TvmazeExternals { Imdb = "tt0903747", TheTvdb = BreakingBadTvdbId, TvRage = 18164 },
            Image = new TvmazeImage { Medium = "https://static.tvmaze.com/uploads/images/medium_portrait/0/2400.jpg", Original = "https://static.tvmaze.com/uploads/images/original_untouched/0/2400.jpg" },
            Embedded = new TvmazeEmbedded
            {
                Seasons =
                {
                    new TvmazeSeason { Id = 1, Number = 1, Name = "Season 1", PremiereDate = "2008-01-20", EndDate = "2008-03-09", EpisodeOrder = 7 }
                },
                Cast =
                {
                    new TvmazeCast { Person = new TvmazePerson { Id = 1, Name = "Bryan Cranston", Image = new TvmazeImage { Medium = "/cranston.jpg", Original = "/cranston_o.jpg" } }, Character = new TvmazePerson { Id = 9, Name = "Walter White" } },
                    new TvmazeCast { Person = new TvmazePerson { Id = 2, Name = "Aaron Paul", Image = new TvmazeImage { Medium = "/aaron.jpg", Original = "/aaron_o.jpg" } }, Character = new TvmazePerson { Id = 8, Name = "Jesse Pinkman" } }
                }
            }
        };
    }

    public static TvmazeShow BlackMirrorNoTvdbExternal()
    {
        return new TvmazeShow
        {
            Id = 96,
            Name = "Black Mirror",
            Language = "English",
            Genres = new List<string> { "Drama", "Science-Fiction" },
            Status = "Running",
            Runtime = 60,
            Premiered = "2011-12-04",
            Summary = "<p>A sci-fi anthology series about the unanticipated consequences of new technologies.</p>",
            Rating = new TvmazeRating { Average = 8.8 },
            Network = new TvmazeNetwork { Id = 2, Name = "Netflix", Country = new TvmazeCountry { Name = "United States", Code = "US" } },
            Externals = new TvmazeExternals { Imdb = "tt2085059", TheTvdb = null, TvRage = 32849 },
            Image = new TvmazeImage { Medium = "/bm_medium.jpg", Original = "/bm_original.jpg" }
        };
    }

    public static List<TvmazeEpisode> BreakingBadTvmazeEpisodes()
    {
        return new List<TvmazeEpisode>
        {
            new()
            {
                Id = 11336,
                Name = "Pilot",
                Season = 1,
                Number = 1,
                Type = "regular",
                AirDate = "2008-01-20",
                Runtime = 60,
                Image = new TvmazeImage { Medium = "/pilot_medium.jpg", Original = "/pilot_original.jpg" },
                Summary = "<p>High school chemistry teacher Walter White is diagnosed with terminal cancer.</p>",
                Rating = new TvmazeRating { Average = 8.2 }
            },
            new()
            {
                Id = 11337,
                Name = "Cat's in the Bag...",
                Season = 1,
                Number = 2,
                Type = "regular",
                AirDate = "2008-01-27",
                Runtime = 60,
                Image = new TvmazeImage { Medium = "/cats_medium.jpg", Original = "/cats_original.jpg" },
                Summary = "<p>Walt and Jesse try to dispose of a body.</p>",
                Rating = new TvmazeRating { Average = 8.4 }
            }
        };
    }

    public static List<TvmazeShow> BreakingBadTvmazeSearchHit()
    {
        return new List<TvmazeShow> { BreakingBadTvmaze() };
    }

    public static TvmazeShow AnimeTvmaze()
    {
        return new TvmazeShow
        {
            Id = 999,
            Name = "Death Note",
            Language = "Japanese",
            Genres = new List<string> { "Drama", "Mystery" },
            Status = "Ended",
            Runtime = 23,
            Premiered = "2006-10-04",
            Summary = "<p>A high-school student stumbles upon a supernatural notebook.</p>",
            Rating = new TvmazeRating { Average = 8.5 },
            Network = new TvmazeNetwork { Id = 3, Name = "NTV", Country = new TvmazeCountry { Name = "Japan", Code = "JP" } },
            Externals = new TvmazeExternals { Imdb = "tt0877057", TheTvdb = 81356 },
            Image = new TvmazeImage { Medium = "https://static.tvmaze.com/uploads/images/medium_portrait/4/3938.jpg", Original = "https://static.tvmaze.com/uploads/images/original_untouched/4/3938.jpg" },
            Embedded = new TvmazeEmbedded
            {
                Seasons =
                {
                    new TvmazeSeason { Id = 1, Number = 1, Name = "Season 1", PremiereDate = "2006-10-04", EndDate = "2007-06-26", EpisodeOrder = 37 }
                }
            }
        };
    }

    public static List<TvmazeEpisode> AnimeTvmazeEpisodes()
    {
        return new List<TvmazeEpisode>
        {
            new()
            {
                Id = 5001,
                Name = "Rebirth",
                Season = 1,
                Number = 1,
                Type = "regular",
                AirDate = "2006-10-04",
                Runtime = 23,
                Image = new TvmazeImage { Medium = "/deathnote_medium.jpg", Original = "/deathnote_original.jpg" },
                Summary = "<p>A bored student finds a deadly notebook.</p>",
                Rating = new TvmazeRating { Average = 8.5 }
            }
        };
    }
}