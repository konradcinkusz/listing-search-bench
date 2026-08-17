namespace Homefinder.SearchService.Endpoints;

/// <summary>
/// Bound from the <c>RateLimit</c> configuration section. Partitioned by caller IP —
/// the crudest fair-use signal available today, because there is no authentication on
/// this endpoint yet (SPEC O-5; a real auth model is a larger, spec-touching change
/// this repository tracks separately, not something this rate limiter substitutes
/// for).
/// </summary>
public sealed class SearchRateLimitOptions
{
    public const string SectionName = "RateLimit";

    public const string PolicyName = "search";

    public int PermitLimit { get; set; } = 60;

    public int WindowSeconds { get; set; } = 60;
}
