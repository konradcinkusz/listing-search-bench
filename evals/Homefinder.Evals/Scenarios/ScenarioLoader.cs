using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Homefinder.Evals.Scenarios;

public sealed record LoadedScenario(string Id, string Path, ScenarioFile Scenario);

/// <summary>Reads every scenario under <c>evals/scenarios/</c> once. The corpus is data — nothing here is scenario-specific.</summary>
public static class ScenarioLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static IReadOnlyList<LoadedScenario> LoadAll()
    {
        if (!Directory.Exists(RepositoryLayout.ScenariosRoot))
        {
            throw new InvalidOperationException(
                $"No scenario directory at '{RepositoryLayout.ScenariosRoot}'. An empty corpus passes every "
                + "gate vacuously, which is worse than a red build — SPEC §8.6.");
        }

        var files = Directory.GetFiles(RepositoryLayout.ScenariosRoot, "*.yaml", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        if (files.Count == 0)
        {
            throw new InvalidOperationException("evals/scenarios/ contains no .yaml files.");
        }

        return [.. files.Select(Load)];
    }

    private static LoadedScenario Load(string path)
    {
        var yaml = File.ReadAllText(path);
        var scenario = Deserializer.Deserialize<ScenarioFile>(yaml)
            ?? throw new InvalidOperationException($"'{path}' deserialized to nothing.");

        if (string.IsNullOrWhiteSpace(scenario.Id))
        {
            throw new InvalidOperationException($"'{path}' has no id.");
        }

        var expectedFileName = $"{scenario.Id}.yaml";

        if (!string.Equals(System.IO.Path.GetFileName(path), expectedFileName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"'{path}' declares id '{scenario.Id}' but is not named '{expectedFileName}'. "
                + "The filename and the id must agree, or a scenario referenced by id in a report "
                + "or a baseline cannot be found on disk.");
        }

        return new LoadedScenario(scenario.Id, path, scenario);
    }
}
