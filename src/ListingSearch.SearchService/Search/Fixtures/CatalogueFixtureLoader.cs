using System.Globalization;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ListingSearch.SearchService.Search.Fixtures;

/// <summary>One resolved fixture: the listing documents a scenario or a dev run starts from.</summary>
public sealed record ResolvedCatalogue(string Name, IReadOnlyList<ListingDocument> Listings);

/// <summary>
/// Reads a fixture YAML file into <see cref="ListingDocument"/>s. This is the one
/// source of truth the default <c>InMemoryFixtureIndex</c> seeds from and that
/// <c>evals/scenarios/*.yaml</c> names by <c>fixture.base</c> — one file, not two that
/// drift.
/// </summary>
public static class CatalogueFixtureLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static ResolvedCatalogue Load(string path)
    {
        using var reader = new StreamReader(path);
        return Parse(reader.ReadToEnd());
    }

    public static ResolvedCatalogue Parse(string yaml)
    {
        var file = Deserializer.Deserialize<CatalogueFixtureFile>(yaml)
            ?? throw new InvalidOperationException("Fixture YAML deserialized to nothing.");

        var listings = file.Listings.Select(ToDocument).ToList();

        return new ResolvedCatalogue(file.Name, listings);
    }

    private static ListingDocument ToDocument(ListingFixtureEntry entry) => new(
        ListingId: entry.Id,
        Title: entry.Title,
        Description: entry.Description,
        City: entry.City,
        PriceChf: entry.PriceChf,
        Rooms: entry.Rooms,
        Status: ParseStatus(entry.Id, entry.Status),
        OwnerId: entry.OwnerId,
        ListedAt: DateTimeOffset.Parse(entry.ListedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

    private static ListingStatus ParseStatus(string listingId, string status) => status switch
    {
        "active" => ListingStatus.Active,
        "draft" => ListingStatus.Draft,
        "delisted" => ListingStatus.Delisted,
        "expired" => ListingStatus.Expired,
        _ => throw new InvalidOperationException(
            $"Listing '{listingId}' has an unrecognised status '{status}'. "
            + "Valid values: active, draft, delisted, expired."),
    };
}
