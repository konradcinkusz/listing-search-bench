using System.Text.Json;
using System.Text.Json.Serialization;

namespace Homefinder.Evals.Reporting;

/// <summary>
/// The recorded pass state a regression is measured against —
/// <c>evals/baselines/layer1.json</c>. Editing it outside a fixture/spec version bump
/// is reviewed with suspicion: a baseline that moves for any other reason is a
/// measuring stick that changed length (SPEC §8.4).
/// </summary>
public sealed class Baseline
{
    public int Layer { get; set; }

    public string SpecVersion { get; set; } = "";

    /// <summary>Which embedding function produced the vector path this baseline was recorded against — "deterministic-hash-v1" today, never silently swapped (ADR-0004).</summary>
    public string Embedding { get; set; } = "";

    public string Recorded { get; set; } = "";

    public Dictionary<string, string> Scenarios { get; set; } = [];

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string StatusOf(string id) => Scenarios.GetValueOrDefault(id, "unrecorded");

    public static Baseline Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<Baseline>(stream, Options)
            ?? throw new InvalidOperationException($"'{path}' deserialized to nothing.");
    }
}
