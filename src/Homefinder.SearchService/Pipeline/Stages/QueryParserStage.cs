using Homefinder.SearchService.Pipeline.Embedding;

namespace Homefinder.SearchService.Pipeline.Stages;

/// <summary>Turns the raw query text into tokens every later stage reads. Always applies.</summary>
public sealed class QueryParserStage : ISearchStage
{
    public string Name => "query_parser";

    public bool AppliesTo(SearchTurnContext context) => true;

    public ValueTask<StageSignal> ExecuteAsync(SearchTurnContext context, CancellationToken cancellationToken)
    {
        context.Tokens = TextTokenizer.Tokenize(context.Request.QueryText);
        return ValueTask.FromResult(StageSignal.Continue);
    }
}
