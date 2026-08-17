namespace ListingSearch.Service.Pipeline.Embedding;

/// <summary>
/// The seam a real embedding model would sit behind — SPEC's vector path calls this,
/// never <see cref="DeterministicTextEmbedding"/> directly, so a trained model can
/// replace <see cref="DeterministicEmbeddingProvider"/> without touching
/// <c>VectorRetrieverStage</c> or <c>ElasticsearchIndex</c> (docs/DEVIATIONS.md D-1).
/// </summary>
public interface IEmbeddingProvider
{
    ValueTask<EmbeddingOutcome> EmbedAsync(string text, CancellationToken cancellationToken = default);
}

/// <summary>
/// Either a vector, or a degradation kind from <c>SearchDiagnostics.DegradationKinds</c>
/// — the same partial-output-with-a-note contract SPEC §7 requires everywhere else, now
/// that computing an embedding is a networked call with its own failure modes rather
/// than a hash function that cannot fail.
/// </summary>
public sealed record EmbeddingOutcome(double[]? Vector, bool Degraded, string? DegradationKind)
{
    public static EmbeddingOutcome Success(double[] vector) => new(vector, Degraded: false, DegradationKind: null);

    public static EmbeddingOutcome Failure(string degradationKind) => new(Vector: null, Degraded: true, DegradationKind: degradationKind);
}

/// <summary>
/// The default, zero-credential <see cref="IEmbeddingProvider"/> — wraps
/// <see cref="DeterministicTextEmbedding"/> so behaviour is byte-for-byte unchanged
/// from before this seam existed. Never degrades: hashing a string in-process cannot
/// fail the way a call to a real provider can.
/// </summary>
public sealed class DeterministicEmbeddingProvider : IEmbeddingProvider
{
    public ValueTask<EmbeddingOutcome> EmbedAsync(string text, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(EmbeddingOutcome.Success(DeterministicTextEmbedding.Compute(text)));
}
