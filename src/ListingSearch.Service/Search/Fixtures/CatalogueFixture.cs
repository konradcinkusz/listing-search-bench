namespace ListingSearch.Service.Search.Fixtures;

/// <summary>The YAML shape of a fixture file under <c>evals/fixtures/</c>, deserialized by YamlDotNet.</summary>
public sealed class CatalogueFixtureFile
{
    public string Name { get; set; } = "";

    public int Version { get; set; } = 1;

    public List<ListingFixtureEntry> Listings { get; set; } = [];

    public List<OwnerFixtureEntry> Owners { get; set; } = [];
}

public sealed class ListingFixtureEntry
{
    public string Id { get; set; } = "";

    public string Title { get; set; } = "";

    public string Description { get; set; } = "";

    public string City { get; set; } = "";

    public decimal PriceChf { get; set; }

    public decimal Rooms { get; set; }

    public string Status { get; set; } = "active";

    public string OwnerId { get; set; } = "";

    public string ListedAt { get; set; } = "2026-01-01T00:00:00Z";
}

public sealed class OwnerFixtureEntry
{
    public string Id { get; set; } = "";

    public string DisplayName { get; set; } = "";
}
