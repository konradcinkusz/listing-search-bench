using ListingSearch.SearchService.Search;

namespace ListingSearch.SearchService.Pipeline.Stages;

/// <summary>
/// Resolves the request's hard filters into one <see cref="SearchIndexFilter"/> —
/// SPEC §2.2. <see cref="SearchIndexFilter.AllowedStatuses"/> is set to <c>[Active]</c>
/// here and nowhere else in this stage is it user-suppliable; that is C-1's structural
/// half. Every retrieval stage still builds its own copy from this one for the index
/// call it makes (SPEC §2.2's independence requirement) — this stage hands each of
/// them the same starting point, not a shared mutable object.
/// </summary>
public sealed class FilterResolverStage : ISearchStage
{
    public string Name => "filter_resolver";

    public bool AppliesTo(SearchTurnContext context) => true;

    public ValueTask<StageSignal> ExecuteAsync(SearchTurnContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;

        context.Filter = new SearchIndexFilter(
            MinPrice: request.MinPrice,
            MaxPrice: request.MaxPrice,
            City: request.City,
            MinRooms: request.MinRooms,
            MaxRooms: request.MaxRooms,
            AllowedStatuses: [ListingStatus.Active]);

        return ValueTask.FromResult(StageSignal.Continue);
    }
}
