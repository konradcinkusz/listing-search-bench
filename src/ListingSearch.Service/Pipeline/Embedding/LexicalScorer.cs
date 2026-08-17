namespace ListingSearch.Service.Pipeline.Embedding;

/// <summary>
/// Term-overlap scoring — deliberately not full BM25 (SPEC §9's assumption, stated
/// rather than implied). Title matches count for more than description matches, and
/// the score is normalised by document length so a long description does not win
/// purely by containing more words.
/// </summary>
public static class LexicalScorer
{
    private const double TitleWeight = 2.0;
    private const double DescriptionWeight = 1.0;

    public static double Score(IReadOnlyList<string> queryTokens, IReadOnlyList<string> titleTokens, IReadOnlyList<string> descriptionTokens)
    {
        if (queryTokens.Count == 0)
        {
            return 0;
        }

        var titleCounts = CountOf(titleTokens);
        var descriptionCounts = CountOf(descriptionTokens);

        double raw = 0;

        foreach (var term in queryTokens)
        {
            raw += TitleWeight * titleCounts.GetValueOrDefault(term);
            raw += DescriptionWeight * descriptionCounts.GetValueOrDefault(term);
        }

        var documentLength = Math.Max(1, titleTokens.Count + descriptionTokens.Count);

        // Length-normalised, then scaled back up so scores stay in a legible range
        // rather than collapsing toward zero for every long description.
        return raw / Math.Sqrt(documentLength) * Math.Sqrt(queryTokens.Count);
    }

    private static Dictionary<string, int> CountOf(IReadOnlyList<string> tokens)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var token in tokens)
        {
            counts[token] = counts.GetValueOrDefault(token) + 1;
        }

        return counts;
    }
}
