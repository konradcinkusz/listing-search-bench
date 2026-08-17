using Homefinder.SearchService.Pipeline.Embedding;

namespace Homefinder.SearchService.Tests;

public class DeterministicTextEmbeddingTests
{
    [Fact]
    public void The_same_text_always_embeds_to_the_same_vector()
    {
        var a = DeterministicTextEmbedding.Compute("Modern apartment near Zurich HB");
        var b = DeterministicTextEmbedding.Compute("Modern apartment near Zurich HB");

        Assert.Equal(a, b);
    }

    [Fact]
    public void Embeddings_have_the_declared_fixed_dimensionality()
    {
        Assert.Equal(DeterministicTextEmbedding.Dimensions, DeterministicTextEmbedding.Compute("anything").Length);
        Assert.Equal(DeterministicTextEmbedding.Dimensions, DeterministicTextEmbedding.Compute("").Length);
    }

    [Fact]
    public void A_vector_is_similar_to_itself()
    {
        var vector = DeterministicTextEmbedding.Compute("Lake-view penthouse Zurich Seefeld");
        var similarity = DeterministicTextEmbedding.CosineSimilarity(vector, vector);

        Assert.True(similarity is > 0.999 and <= 1.0001);
    }

    [Fact]
    public void Identical_texts_are_more_similar_than_unrelated_texts()
    {
        var query = DeterministicTextEmbedding.Compute("modern apartment zurich");
        var close = DeterministicTextEmbedding.Compute("modern apartment near zurich hb");
        var far = DeterministicTextEmbedding.Compute("charmante maison avec jardin geneve");

        var closeSimilarity = DeterministicTextEmbedding.CosineSimilarity(query, close);
        var farSimilarity = DeterministicTextEmbedding.CosineSimilarity(query, far);

        Assert.True(closeSimilarity > farSimilarity);
    }

    [Fact]
    public void Mismatched_dimensions_throw_rather_than_silently_compare_nonsense()
    {
        var vector = DeterministicTextEmbedding.Compute("anything");
        Assert.Throws<ArgumentException>(() => DeterministicTextEmbedding.CosineSimilarity(vector, [0.1, 0.2]));
    }
}
