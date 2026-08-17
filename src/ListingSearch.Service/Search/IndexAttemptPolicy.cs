namespace ListingSearch.Service.Search;

public interface IIndexAttemptPolicy
{
    int MaxAttempts(string operation);

    bool IsWorthRetrying(bool degraded, string? degradationKind);
}

/// <summary>
/// Writes are hard-capped at one attempt — retrying <c>IndexAsync</c>/<c>DeleteAsync</c>
/// is not resilience, it is SPEC C-6's failure mode (an ingestion event applied twice).
/// Reads get a small configurable cap, and only a genuine failure is worth a second
/// attempt: a <c>timeout</c> is retried past the injected instant is never honoured
/// (SPEC §7.1), so in practice only <c>shard_unavailable</c> triggers a second read.
/// </summary>
public sealed class IndexAttemptPolicy(int maxReadAttempts) : IIndexAttemptPolicy
{
    public int MaxAttempts(string operation) =>
        SearchIndexOperationCatalog.IsWrite(operation) ? 1 : Math.Max(1, maxReadAttempts);

    public bool IsWorthRetrying(bool degraded, string? degradationKind) =>
        degraded && string.Equals(degradationKind, "shard_unavailable", StringComparison.Ordinal);
}
