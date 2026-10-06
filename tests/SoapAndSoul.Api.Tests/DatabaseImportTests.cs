using Microsoft.EntityFrameworkCore;
using SoapAndSoul.Data;

namespace SoapAndSoul.Api.Tests;

/// <summary><c>Database:ImportFrom</c>: the one-time copy of an old database into a new, empty one at startup.</summary>
public class DatabaseImportTests
{
    [Fact]
    public async Task Startup_copies_every_row_before_seeding_and_only_once()
    {
        await using var source = new ApiFactory { SeedSampleData = true };
        var http = source.CreateClient();
        var deleted = await source.WithDbAsync(db => db.Ingredients.OrderBy(i => i.Id).Select(i => i.Id).FirstAsync());
        (await http.DeleteAsync($"/api/ingredients/{deleted}")).EnsureSuccessStatusCode();
        var expected = await source.WithDbAsync(Rows);

        // The catalog is seeded right after the import; seeding first would leave the target non-empty and skip it.
        await using var target = new ApiFactory { SeedCatalog = true, ImportFrom = source.ConnectionString };
        target.CreateClient();

        var actual = await target.WithDbAsync(Rows);
        Assert.Contains($"{deleted}|", actual.Ingredients);
        Assert.NotEmpty(actual.Recipes);
        Assert.Equal(expected, actual);

        await using var again = new SoapAndSoulDbContext(new DbContextOptionsBuilder<SoapAndSoulDbContext>()
            .UseSqlite(source.ConnectionString).Options);
        Assert.Null(await target.WithDbAsync(db => DatabaseImport.CopyIfEmptyAsync(again, db)));
    }

    private sealed record Snapshot(string Ingredients, string Recipes);

    /// <summary>Every column of every row, tombstones included, as comparable text.</summary>
    private static async Task<Snapshot> Rows(SoapAndSoulDbContext db)
    {
        var ingredients = await db.Ingredients.IgnoreQueryFilters().AsNoTracking().OrderBy(i => i.Id).ToListAsync();
        var recipes = await db.Recipes.IgnoreQueryFilters().AsNoTracking().Include(r => r.Items).OrderBy(r => r.Id).ToListAsync();
        return new Snapshot(
            string.Join("\n", ingredients.Select(i =>
                $"{i.Id}|{i.Version}|{i.CreatedAt:O}|{i.UpdatedAt:O}|{i.IsDeleted}|{i.Line}|{i.Category}|{i.Name}|{i.Unit}|" +
                $"{i.TypicalAmount}|{i.PurchaseQuantity}|{i.PurchasePrice}|{i.Capacity}|{i.UsesPerItem}|{i.PhotoUrl}|{i.InStock}")),
            string.Join("\n", recipes.Select(r =>
                $"{r.Id}|{r.Version}|{r.CreatedAt:O}|{r.UpdatedAt:O}|{r.IsDeleted}|{r.Line}|{r.Name}|{r.Description}|{r.Weight}|" +
                $"{r.TimeMinutes}|{r.BatchSize}|{r.PhotoUrl}|" +
                string.Join(";", r.Items.OrderBy(x => x.Position).Select(x => $"{x.IngredientId}:{x.Amount}:{x.Position}")))));
    }
}
