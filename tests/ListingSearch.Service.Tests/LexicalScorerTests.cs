using ListingSearch.Service.Pipeline.Embedding;

namespace ListingSearch.Service.Tests;

public class LexicalScorerTests
{
    [Fact]
    public void No_query_tokens_scores_zero()
    {
        var score = LexicalScorer.Score([], ["modern", "apartment"], ["bright", "modern"]);
        Assert.Equal(0, score);
    }

    [Fact]
    public void No_overlap_scores_zero()
    {
        var score = LexicalScorer.Score(["penthouse"], ["modern", "apartment"], ["bright", "flat"]);
        Assert.Equal(0, score);
    }

    [Fact]
    public void A_title_match_scores_higher_than_the_same_word_only_in_the_description()
    {
        var titleMatch = LexicalScorer.Score(["modern"], ["modern", "apartment"], []);
        var descriptionOnlyMatch = LexicalScorer.Score(["modern"], ["cosy", "flat"], ["a", "modern", "kitchen"]);

        Assert.True(titleMatch > descriptionOnlyMatch);
    }

    [Fact]
    public void More_matching_terms_scores_at_least_as_high_as_fewer()
    {
        var oneTerm = LexicalScorer.Score(["modern"], ["modern", "apartment", "zurich"], []);
        var twoTerms = LexicalScorer.Score(["modern", "zurich"], ["modern", "apartment", "zurich"], []);

        Assert.True(twoTerms > oneTerm);
    }
}
