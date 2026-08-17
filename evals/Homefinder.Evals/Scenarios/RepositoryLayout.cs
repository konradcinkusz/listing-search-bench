namespace Homefinder.Evals.Scenarios;

/// <summary>
/// Where the corpus lives at test time. The build links <c>evals/{schema,fixtures,
/// scenarios,baselines,rubrics,calibration}</c> into <c>EvalsData/</c> under the
/// output directory (Homefinder.Evals.csproj) rather than requiring the test process
/// to walk up to a repository root — the same file CI validates with
/// <c>scripts/validate-scenarios.mjs</c> is the file this reads.
/// </summary>
public static class RepositoryLayout
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "EvalsData");

    public static string ScenariosRoot => Path.Combine(Data, "scenarios");

    public static string FixturesRoot => Path.Combine(Data, "fixtures");

    public static string SchemaRoot => Path.Combine(Data, "schema");

    public static string BaselinesRoot => Path.Combine(Data, "baselines");

    public static string RubricsRoot => Path.Combine(Data, "rubrics");

    public static string CalibrationRoot => Path.Combine(Data, "calibration");

    public static string FixturePath(string name) => Path.Combine(FixturesRoot, $"{name}.yaml");
}
