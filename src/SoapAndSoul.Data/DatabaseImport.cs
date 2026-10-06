using Microsoft.EntityFrameworkCore;

namespace SoapAndSoul.Data;

/// <summary>
/// One-time copy of every row from another database into an empty one, used to move production to a new
/// Azure SQL database (<c>Database:ImportFrom</c>). Rows keep their ids, timestamps, versions and tombstones.
/// </summary>
public static class DatabaseImport
{
    public sealed record Result(int Ingredients, int Recipes);

    /// <summary>Copies <paramref name="source"/> into <paramref name="target"/>; returns null when the target already has data.</summary>
    public static async Task<Result?> CopyIfEmptyAsync(SoapAndSoulDbContext source, SoapAndSoulDbContext target, CancellationToken ct = default)
    {
        if (await target.Ingredients.IgnoreQueryFilters().AnyAsync(ct) || await target.Recipes.IgnoreQueryFilters().AnyAsync(ct))
            return null;

        var pending = await source.Database.GetPendingMigrationsAsync(ct);
        if (pending.Any())
            throw new InvalidOperationException(
                $"The import source is missing migrations ({string.Join(", ", pending)}); start the old app once to apply them.");

        var ingredients = await source.Ingredients.IgnoreQueryFilters().AsNoTracking().ToListAsync(ct);
        var recipes = await source.Recipes.IgnoreQueryFilters().AsNoTracking().Include(r => r.Items).ToListAsync(ct);

        target.Ingredients.AddRange(ingredients);
        target.Recipes.AddRange(recipes);
        // One SaveChanges is one transaction: a failed import leaves the target empty and is retried on the next start.
        await target.SaveChangesAsync(ct);
        target.ChangeTracker.Clear();
        return new Result(ingredients.Count, recipes.Count);
    }
}
