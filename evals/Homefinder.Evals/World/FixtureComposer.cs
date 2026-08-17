using Homefinder.Evals.Scenarios;
using Homefinder.SearchService.Search;
using Homefinder.SearchService.Search.Fixtures;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Homefinder.Evals.World;

public sealed record EvalWorld(string FixtureName, IReadOnlyList<ListingDocument> Listings);

/// <summary>
/// Builds the world one scenario runs against: the named base fixture plus the
/// scenario's own <c>fixture.overrides</c> — a shared fictional catalogue plus a
/// per-scenario delta, the same shape <c>evals/fixtures/</c>'s README documents for
/// the worked example this repository mirrors. An override entry with an id already
/// in the base fixture replaces it; a new id adds a listing.
/// </summary>
public static class FixtureComposer
{
    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    public static EvalWorld Compose(LoadedScenario loaded)
    {
        var fixture = loaded.Scenario.Fixture;
        var path = RepositoryLayout.FixturePath(fixture.Base);

        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Scenario '{loaded.Id}' names fixture.base '{fixture.Base}', which does not exist at '{path}'.");
        }

        var baseCatalogue = CatalogueFixtureLoader.Load(path);
        var byId = baseCatalogue.Listings.ToDictionary(l => l.ListingId, StringComparer.Ordinal);

        foreach (var overrideEntry in fixture.Overrides)
        {
            var entry = ToEntry(loaded.Id, overrideEntry);
            byId[entry.Id] = CatalogueFixtureLoader.Parse(WrapAsSingleListingFixture(entry)).Listings[0];
        }

        return new EvalWorld(fixture.Base, [.. byId.Values.OrderBy(l => l.ListingId, StringComparer.Ordinal)]);
    }

    private static ListingFixtureEntry ToEntry(string scenarioId, Dictionary<string, object> raw)
    {
        var yaml = Serializer.Serialize(raw);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var entry = deserializer.Deserialize<ListingFixtureEntry>(yaml)
            ?? throw new InvalidOperationException($"Scenario '{scenarioId}' has an override that deserialized to nothing.");

        if (string.IsNullOrWhiteSpace(entry.Id))
        {
            throw new InvalidOperationException($"Scenario '{scenarioId}' has a fixture override with no 'id'.");
        }

        return entry;
    }

    private static string WrapAsSingleListingFixture(ListingFixtureEntry entry)
    {
        var yaml = Serializer.Serialize(new CatalogueFixtureFile { Name = "override", Listings = [entry] });
        return yaml;
    }
}
