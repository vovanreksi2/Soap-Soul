using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SoapAndSoul.Data.Catalog;
using SoapAndSoul.Domain.Catalog;
using SoapAndSoul.Domain.Contracts;
using SoapAndSoul.Domain.Validation;

namespace SoapAndSoul.Api.Tests;

/// <summary>The supplier catalog seeded as ingredients (<c>tools/aromasoap/scrape.py</c> builds it).</summary>
public class CatalogSeedTests
{
    [Fact]
    public void Every_catalog_entry_is_a_valid_ingredient()
    {
        Assert.NotEmpty(CatalogSeed.Entries);
        Assert.All(CatalogSeed.Entries, e =>
        {
            var dto = new IngredientDto(e.Id, e.Line, e.Category, e.Name, e.Unit, e.TypicalAmount, e.PurchaseQuantity,
                e.PurchasePrice, e.Capacity, e.UsesPerItem, e.PhotoUrl);
            Assert.True(IngredientValidator.Validate(dto).IsValid, e.Name);
        });
        Assert.Equal(CatalogSeed.Entries.Count, CatalogSeed.Entries.Select(e => e.Id).Distinct().Count());
    }

    [Fact]
    public void Catalog_covers_every_scraped_category()
    {
        var categories = CatalogSeed.Entries.Select(e => e.Category).ToHashSet();

        Assert.Superset(categories, new HashSet<CategoryKey>
        {
            CategoryKey.Mold, CategoryKey.SoapBase, CategoryKey.Pigment, CategoryKey.Fragrance, CategoryKey.Extract,
        });
    }

    [Fact]
    public async Task Seeding_adds_the_catalog_once_out_of_stock_and_keeps_user_deletions()
    {
        await using var factory = new ApiFactory { SeedCatalog = true };
        var http = factory.CreateClient();
        var soap = await http.GetFromJsonAsync<List<IngredientDto>>("/api/ingredients?line=Soap");
        Assert.Equal(CatalogSeed.Entries.Count, soap!.Count);
        Assert.All(soap, i => Assert.False(i.InStock, i.Name));

        var deleted = soap[0];
        (await http.DeleteAsync($"/api/ingredients/{deleted.Id}")).EnsureSuccessStatusCode();
        var added = await factory.WithDbAsync(db => CatalogSeed.SeedAsync(db));

        Assert.Equal(0, added);
        Assert.False(await factory.WithDbAsync(db => db.Ingredients.AnyAsync(i => i.Id == deleted.Id)));
    }
}
