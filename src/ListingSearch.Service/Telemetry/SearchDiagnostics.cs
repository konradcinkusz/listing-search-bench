using System.Diagnostics;

namespace ListingSearch.Service.Telemetry;

/// <summary>
/// The trace vocabulary, as data. Every span name, attribute key and event name the
/// pipeline emits is defined exactly once here, and <c>docs/SPEC.md</c> §2.3 is the
/// document that makes each one a contract rather than a logging convenience.
/// </summary>
public static class SearchDiagnostics
{
    public const string ActivitySourceName = "ListingSearch.Service";

    public static readonly ActivitySource Source = new(ActivitySourceName);

    public static class Events
    {
        public const string FilterRejected = "filter.rejected";
        public const string ConstraintViolated = "constraint.violated";
        public const string IngestionApplied = "ingestion.applied";
        public const string IngestionDuplicateIgnored = "ingestion.duplicate_ignored";
        public const string IngestionFailed = "ingestion.failed";
        public const string IngestionDeferred = "ingestion.deferred";
        public const string IngestionDeadLettered = "ingestion.dead_lettered";
        public const string RankingManipulationIgnored = "ranking.manipulation_ignored";
        public const string DegradationNoted = "degradation.noted";
        public const string Attempt = "attempt";
    }

    public static class Attributes
    {
        public const string StageName = "search.stage.name";
        public const string StageApplied = "search.stage.applied";
        public const string TurnOutcome = "search.turn.outcome";
        public const string TerminationReason = "search.termination.reason";

        public const string IndexOperation = "search_index.operation";
        public const string IndexKind = "search_index.kind";
        public const string IndexCandidateCount = "search_index.candidate_count";
        public const string IndexResultIds = "search_index.result_ids";

        public const string FilterCity = "filter.city";
        public const string FilterMinPrice = "filter.min_price";
        public const string FilterMaxPrice = "filter.max_price";

        public const string ResultListingId = "result.listing_id";
        public const string ResultSource = "result.source";
        public const string ResultScore = "result.score";

        public const string DegradationStage = "degradation.stage";
        public const string DegradationKind = "degradation.kind";

        public const string IngestionEventId = "ingestion.event_id";
        public const string IngestionEventType = "ingestion.event_type";
        public const string IngestionListingId = "ingestion.listing_id";
        public const string IngestionDeadLetterReason = "ingestion.dead_letter_reason";

        public const string ManipulationListingId = "ranking.manipulation.listing_id";
        public const string ManipulationSignal = "ranking.manipulation.signal";

        public const string AttemptNumber = "attempt.number";
        public const string AttemptOutcome = "attempt.outcome";
    }

    /// <summary>The closed set of degradation stages SPEC §2.3 names — nothing outside it.</summary>
    public static class DegradationStages
    {
        public const string LexicalRetrieval = "lexical_retrieval";
        public const string VectorRetrieval = "vector_retrieval";
        public const string FilterResolution = "filter_resolution";
        public const string Ingestion = "ingestion";
    }

    public static class DegradationKinds
    {
        public const string Timeout = "timeout";
        public const string ShardUnavailable = "shard_unavailable";
        public const string MalformedEmbedding = "malformed_embedding";
        public const string Empty = "empty";
    }

    public static class TerminationReasons
    {
        public const string Resolved = "resolved";
        public const string Degraded = "degraded";
    }
}
