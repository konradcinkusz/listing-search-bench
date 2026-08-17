namespace ListingSearch.Service.Pipeline;

public enum StageSignal
{
    Continue,
    Stop,
}

/// <summary>
/// One stage of the search pipeline. <see cref="AppliesTo"/> is checked before
/// <see cref="ExecuteAsync"/> so "did not apply" and "ran and did nothing" stay
/// distinguishable in the trace (<c>search.stage.applied</c> is set either way).
/// <see cref="Name"/> is stable and appears in spans — renaming it is a contract
/// change.
/// </summary>
public interface ISearchStage
{
    string Name { get; }

    bool AppliesTo(SearchTurnContext context);

    ValueTask<StageSignal> ExecuteAsync(SearchTurnContext context, CancellationToken cancellationToken);
}
