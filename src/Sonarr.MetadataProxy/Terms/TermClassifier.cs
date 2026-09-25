namespace Sonarr.MetadataProxy.Terms;

public enum TermKind
{
    Title,
    TvdbId,
    ImdbId,
    TmdbId,
    AniListId,
    MalId,
    TvmazeId,
    TvdbSearch,
    TmdbSearch,
    TvmazeSearch
}

public sealed record SearchTerm(TermKind Kind, string Value, string Raw);

public static class TermClassifier
{
    public static SearchTerm Classify(string raw)
    {
        var value = (raw ?? string.Empty).Trim();
        var lowered = value.ToLowerInvariant();

        if (lowered.StartsWith("tvdbid:"))
        {
            return ParseInt(lowered, "tvdbid:", TermKind.TvdbId, lowered);
        }

        if (lowered.StartsWith("tvdb:"))
        {
            return ParseIdOrSearch(lowered, "tvdb:", TermKind.TvdbId, TermKind.TvdbSearch, lowered);
        }

        if (lowered.StartsWith("tmdb:"))
        {
            return ParseIdOrSearch(lowered, "tmdb:", TermKind.TmdbId, TermKind.TmdbSearch, lowered);
        }

        if (lowered.StartsWith("mal:"))
        {
            return ParseInt(lowered, "mal:", TermKind.MalId, lowered);
        }

        if (lowered.StartsWith("anilist:"))
        {
            return ParseInt(lowered, "anilist:", TermKind.AniListId, lowered);
        }

        if (lowered.StartsWith("tvmaze:"))
        {
            return ParseIdOrSearch(lowered, "tvmaze:", TermKind.TvmazeId, TermKind.TvmazeSearch, lowered);
        }

        if (lowered.StartsWith("imdb:"))
        {
            var imdbId = value.Substring("imdb:".Length).Trim();
            return new SearchTerm(TermKind.ImdbId, imdbId, value);
        }

        if (int.TryParse(value, out var numericId) && numericId > 0)
        {
            return new SearchTerm(TermKind.TvdbId, numericId.ToString(), value);
        }

        return new SearchTerm(TermKind.Title, value, value);
    }

    private static SearchTerm ParseInt(string lowered, string prefix, TermKind kind, string original)
    {
        var segment = lowered.Substring(prefix.Length).Trim();
        if (int.TryParse(segment, out var id) && id > 0)
        {
            return new SearchTerm(kind, id.ToString(), original);
        }

        return new SearchTerm(TermKind.Title, lowered, original);
    }

    private static SearchTerm ParseIdOrSearch(string lowered, string prefix, TermKind idKind, TermKind searchKind, string original)
    {
        var segment = lowered.Substring(prefix.Length).Trim();
        if (int.TryParse(segment, out var id) && id > 0)
        {
            return new SearchTerm(idKind, id.ToString(), original);
        }

        return new SearchTerm(searchKind, segment, original);
    }
}