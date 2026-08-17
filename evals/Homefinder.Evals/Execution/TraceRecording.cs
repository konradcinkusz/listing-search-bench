using System.Diagnostics;
using Homefinder.SearchService.Search;
using Homefinder.SearchService.Telemetry;

namespace Homefinder.Evals.Execution;

public sealed record IndexCallRecord(
    int Position,
    string Operation,
    string Kind,
    IReadOnlyList<string> CandidateListingIds,
    int Attempts,
    bool Degraded,
    string? DegradationKind,
    IReadOnlyDictionary<string, object?> Tags);

public sealed record StageRecord(int Position, string Name, bool Applied, IReadOnlyDictionary<string, object?> Tags);

public sealed record TraceEventRecord(int Position, string Name, IReadOnlyDictionary<string, object?> Tags);

/// <summary>
/// One scenario's captured trace, reshaped from raw <see cref="Activity"/> objects
/// into the three lists <c>AssertionEvaluator</c> reads. Deliberately does not expose
/// <see cref="Activity"/> itself past this file — every assertion reads a plain
/// record, never the OTel SDK's own types, so a version bump of the SDK cannot change
/// what a scenario asserts against.
/// </summary>
public sealed record TraceRecording(
    IReadOnlyList<IndexCallRecord> IndexCalls,
    IReadOnlyList<StageRecord> Stages,
    IReadOnlyList<TraceEventRecord> Events)
{
    public static TraceRecording From(IEnumerable<Activity> activities)
    {
        var ordered = activities
            .OrderBy(activity => activity.StartTimeUtc)
            .ToList();

        var indexCalls = new List<IndexCallRecord>();
        var stages = new List<StageRecord>();
        var events = new List<TraceEventRecord>();
        var position = 0;

        foreach (var activity in ordered)
        {
            var tags = Tags(activity);

            if (activity.OperationName.StartsWith("search_index ", StringComparison.Ordinal))
            {
                var operation = (string?)tags.GetValueOrDefault(SearchDiagnostics.Attributes.IndexOperation) ?? "";

                indexCalls.Add(new IndexCallRecord(
                    position++,
                    operation,
                    (string?)tags.GetValueOrDefault(SearchDiagnostics.Attributes.IndexKind) ?? "",
                    SplitIds(tags.GetValueOrDefault(SearchDiagnostics.Attributes.IndexResultIds)),
                    activity.Events.Count(e => string.Equals(e.Name, SearchDiagnostics.Events.Attempt, StringComparison.Ordinal)),
                    activity.Status == ActivityStatusCode.Error,
                    activity.StatusDescription,
                    tags));
            }
            else if (activity.OperationName.StartsWith("search_stage ", StringComparison.Ordinal))
            {
                stages.Add(new StageRecord(
                    position++,
                    (string?)tags.GetValueOrDefault(SearchDiagnostics.Attributes.StageName) ?? "",
                    tags.GetValueOrDefault(SearchDiagnostics.Attributes.StageApplied) is true,
                    tags));
            }
            else
            {
                position++;
            }

            foreach (var activityEvent in activity.Events.OrderBy(e => e.Timestamp))
            {
                if (string.Equals(activityEvent.Name, SearchDiagnostics.Events.Attempt, StringComparison.Ordinal))
                {
                    // Counted on the owning span (IndexCallRecord.Attempts), never
                    // ordered as its own event — the same rule SPEC §2.4 states for
                    // an index call's transport retries.
                    continue;
                }

                events.Add(new TraceEventRecord(position++, activityEvent.Name, EventTags(activityEvent)));
            }
        }

        return new TraceRecording(indexCalls, stages, events);
    }

    private static IReadOnlyDictionary<string, object?> Tags(Activity activity) =>
        activity.TagObjects.ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, object?> EventTags(ActivityEvent activityEvent) =>
        activityEvent.Tags.ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal);

    private static IReadOnlyList<string> SplitIds(object? value) =>
        value is string text && !string.IsNullOrWhiteSpace(text)
            ? [.. text.Split(';', StringSplitOptions.RemoveEmptyEntries)]
            : [];
}
