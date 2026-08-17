namespace Homefinder.SearchService.Pipeline.Embedding;

/// <summary>
/// A deterministic, zero-credential stand-in for a trained sentence embedding —
/// feature hashing (the "hashing trick"), not a learned representation. Every token
/// hashes, by a fixed non-cryptographic function, to one of <see cref="Dimensions"/>
/// buckets with a fixed sign, and the token counts accumulate into an L2-normalised
/// vector.
///
/// <para>
/// This is named plainly rather than dressed up: it proves the pipeline's <em>plumbing</em>
/// — that a vector path exists, is scored, is filtered before scoring, and is merged
/// correctly with the lexical path — and it proves nothing about ranking quality
/// against a real embedding model (docs/DEVIATIONS.md D-1). What it must not be is
/// <see cref="string.GetHashCode()"/>: that hash is randomised per process by design,
/// so the same listing would embed to a different vector on every CI run, and the
/// suite would grade differently depending on when it happened to execute — the
/// nondeterminism <c>AI-EVALS.md</c> §9 names as the standard failure mode. FNV-1a
/// over UTF-8 bytes is fixed for the lifetime of this file.
/// </para>
/// </summary>
public static class DeterministicTextEmbedding
{
    public const int Dimensions = 24;

    public static double[] Compute(string text) => Compute(TextTokenizer.Tokenize(text));

    public static double[] Compute(IReadOnlyList<string> tokens)
    {
        var vector = new double[Dimensions];

        foreach (var token in tokens)
        {
            var hash = Fnv1A(token);
            var bucket = (int)(hash % Dimensions);
            var sign = (hash & 1) == 0 ? 1.0 : -1.0;
            vector[bucket] += sign;
        }

        var norm = Math.Sqrt(vector.Sum(v => v * v));

        if (norm < 1e-9)
        {
            return vector;
        }

        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] /= norm;
        }

        return vector;
    }

    public static double CosineSimilarity(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        if (a.Count != b.Count)
        {
            throw new ArgumentException(
                $"Embedding dimension mismatch: {a.Count} vs {b.Count}. Every embedding in this "
                + "service comes from DeterministicTextEmbedding.Compute, so a mismatch means one "
                + "side was constructed some other way.");
        }

        double dot = 0, normA = 0, normB = 0;

        for (var i = 0; i < a.Count; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA < 1e-9 || normB < 1e-9)
        {
            return 0;
        }

        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    /// <summary>32-bit FNV-1a. Fixed, unseeded, the same on every run and every machine.</summary>
    private static uint Fnv1A(string token)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;

        var hash = offsetBasis;

        foreach (var b in System.Text.Encoding.UTF8.GetBytes(token))
        {
            hash ^= b;
            hash *= prime;
        }

        return hash;
    }
}
