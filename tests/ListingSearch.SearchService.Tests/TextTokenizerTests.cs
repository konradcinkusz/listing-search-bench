using ListingSearch.SearchService.Pipeline.Embedding;

namespace ListingSearch.SearchService.Tests;

public class TextTokenizerTests
{
    [Fact]
    public void Lowercases_and_splits_on_punctuation()
    {
        var tokens = TextTokenizer.Tokenize("Modern 3.5-room Apartment, Zurich!");
        Assert.Equal(["modern", "3", "5", "room", "apartment", "zurich"], tokens);
    }

    [Fact]
    public void Strips_diacritics_so_accented_and_plain_forms_match()
    {
        Assert.Equal(TextTokenizer.Tokenize("café"), TextTokenizer.Tokenize("cafe"));
        Assert.Equal(TextTokenizer.Tokenize("Genève"), TextTokenizer.Tokenize("Geneve"));
    }

    [Fact]
    public void Empty_or_whitespace_text_tokenises_to_nothing()
    {
        Assert.Empty(TextTokenizer.Tokenize(""));
        Assert.Empty(TextTokenizer.Tokenize("   "));
    }

    [Fact]
    public void German_umlaut_words_tokenise_without_throwing()
    {
        var tokens = TextTokenizer.Tokenize("Gemütliche 4-Zimmer-Wohnung");
        Assert.Contains("zimmer", tokens);
        Assert.Contains("wohnung", tokens);
    }
}
