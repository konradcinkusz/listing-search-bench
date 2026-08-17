using System.Security.Cryptography;
using System.Text;
using Homefinder.Evals.Scenarios;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Homefinder.Evals.Judging;

public sealed class RubricDefinition
{
    public int Scale { get; set; }

    public double Threshold { get; set; }

    public double? Floor { get; set; }

    public string AppliesTo { get; set; } = "any";

    public string Summary { get; set; } = "";

    public Dictionary<int, string> Anchors { get; set; } = [];
}

public sealed class CalibrationGateConfig
{
    public int MinimumLabels { get; set; } = 40;

    public int MinimumScenarios { get; set; } = 8;

    public double MinimumKappa { get; set; } = 0.6;
}

public sealed class SmokeEntry
{
    public string Id { get; set; } = "";

    public string Why { get; set; } = "";
}

internal sealed class JudgeConfigurationFile
{
    public string Version { get; set; } = "";

    public string SpecSection { get; set; } = "";

    public CalibrationGateConfig Calibration { get; set; } = new();

    public Dictionary<string, RubricDefinition> Rubrics { get; set; } = [];

    public List<SmokeEntry> Smoke { get; set; } = [];
}

/// <summary>
/// The judge's pinned configuration — SPEC §5, ADR-0004. <see cref="RubricsHash"/> and
/// <see cref="PromptHash"/> are computed once at load time from the files' own bytes
/// (newline-normalised so a checkout on a different platform hashes identically),
/// and both travel with every judge run: a rubric or prompt edit is a version bump
/// whether or not the number in the file's header was remembered to move with it.
///
/// <para>
/// The judge <em>model</em> is deliberately not pinned here — it is read back from
/// what the provider actually reports on each response
/// (<see cref="Llm.LlmResponse.Model"/>), never assumed from configuration, so a
/// score is always attributable to the model that produced it (ADR-0004).
/// </para>
/// </summary>
public sealed class JudgeConfiguration
{
    private readonly Dictionary<string, RubricDefinition> _rubrics;

    private JudgeConfiguration(
        string version, string specSection, CalibrationGateConfig calibration,
        Dictionary<string, RubricDefinition> rubrics, IReadOnlyList<SmokeEntry> smoke,
        string rubricsHash, string promptHash, string promptTemplate)
    {
        Version = version;
        SpecSection = specSection;
        Calibration = calibration;
        _rubrics = rubrics;
        Smoke = smoke;
        RubricsHash = rubricsHash;
        PromptHash = promptHash;
        PromptTemplate = promptTemplate;
    }

    public static Lazy<JudgeConfiguration> Instance { get; } = new(Load);

    public string Version { get; }

    public string SpecSection { get; }

    public CalibrationGateConfig Calibration { get; }

    public IReadOnlyList<SmokeEntry> Smoke { get; }

    public string RubricsHash { get; }

    public string PromptHash { get; }

    public string PromptTemplate { get; }

    public IReadOnlyCollection<string> RubricNames => _rubrics.Keys;

    public RubricDefinition this[string name] =>
        _rubrics.TryGetValue(name, out var rubric)
            ? rubric
            : throw new KeyNotFoundException($"'{name}' is not a rubric in evals/rubrics/judge.yaml.");

    /// <summary>
    /// Fills the prompt template's two placeholders — the rubric block (highest
    /// anchor first, named rubrics only) and the transcript — by substitution, never
    /// a templating engine, matching <c>judge-prompt.md</c>'s own stated shape.
    /// </summary>
    public string BuildPrompt(IReadOnlyList<string> rubricNames, string transcript)
    {
        var rubricBlock = string.Join(
            "\n\n",
            rubricNames.Select(name =>
            {
                var rubric = this[name];
                var anchors = string.Join(
                    "\n",
                    Enumerable.Range(0, rubric.Scale + 1).Reverse()
                        .Select(level => $"- {level}: {rubric.Anchors.GetValueOrDefault(level, "(no anchor recorded)")}"));

                return $"### {name} (0–{rubric.Scale}, threshold {rubric.Threshold})\n{rubric.Summary}\n{anchors}";
            }));

        return PromptTemplate.Replace("{{RUBRICS}}", rubricBlock, StringComparison.Ordinal)
            .Replace("{{TRANSCRIPT}}", transcript, StringComparison.Ordinal);
    }

    private static JudgeConfiguration Load()
    {
        var rubricsPath = Path.Combine(RepositoryLayout.RubricsRoot, "judge.yaml");
        var promptPath = Path.Combine(RepositoryLayout.RubricsRoot, "judge-prompt.md");

        var rubricsYaml = File.ReadAllText(rubricsPath);
        var promptText = File.ReadAllText(promptPath);

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var file = deserializer.Deserialize<JudgeConfigurationFile>(rubricsYaml)
            ?? throw new InvalidOperationException($"'{rubricsPath}' deserialized to nothing.");

        foreach (var (name, rubric) in file.Rubrics)
        {
            for (var level = 0; level <= rubric.Scale; level++)
            {
                if (!rubric.Anchors.ContainsKey(level))
                {
                    throw new InvalidOperationException(
                        $"Rubric '{name}' has scale {rubric.Scale} but no anchor for level {level}. "
                        + "Every rubric requires one anchor string per integer level, or a judge score "
                        + "at that level has no meaning to grade against.");
                }
            }
        }

        return new JudgeConfiguration(
            file.Version, file.SpecSection, file.Calibration, file.Rubrics, file.Smoke,
            Hash(rubricsYaml), Hash(promptText), promptText);
    }

    /// <summary>SHA-256, first 12 hex characters, newline-normalised so a Windows checkout hashes identically to a Linux one.</summary>
    private static string Hash(string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexStringLower(bytes)[..12];
    }
}
