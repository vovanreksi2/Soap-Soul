using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SoapAndSoul.Data.Entities;
using SoapAndSoul.Domain.Catalog;

namespace SoapAndSoul.Data.Catalog;

/// <summary>
/// Supplier catalog (<c>aromasoap.json</c>, built by <c>tools/aromasoap/scrape.py</c>) seeded as ingredients.
/// Ids are derived from the product URL, so seeding is idempotent and never brings back
/// an entry the user has edited, renamed or deleted; new catalog entries are added on the next start.
/// </summary>
public static class CatalogSeed
{
    public sealed record Entry(
        CosmeticLine Line, CategoryKey Category, string Name, MeasureUnit Unit, decimal TypicalAmount,
        decimal PurchaseQuantity, decimal PurchasePrice, decimal? Capacity, int UsesPerItem, string? PhotoUrl, string Source)
    {
        public Guid Id { get; } = IdFor(Source);
    }

    private static readonly Lazy<IReadOnlyList<Entry>> Catalog = new(Load);

    public static IReadOnlyList<Entry> Entries => Catalog.Value;

    /// <summary>Adds catalog entries that were never stored; returns how many were added.</summary>
    public static async Task<int> SeedAsync(SoapAndSoulDbContext db, CancellationToken ct = default)
    {
        var ids = Entries.Select(e => e.Id).ToList();
        var existing = await db.Ingredients.IgnoreQueryFilters().Where(i => ids.Contains(i.Id)).Select(i => i.Id).ToListAsync(ct);
        var missing = Entries.ExceptBy(existing, e => e.Id).ToList();
        if (missing.Count == 0) return 0;

        var now = DateTimeOffset.UtcNow;
        db.Ingredients.AddRange(missing.Select(e => new Ingredient
        {
            Id = e.Id, Line = e.Line, Category = e.Category, Name = e.Name, Unit = e.Unit, TypicalAmount = e.TypicalAmount,
            PurchaseQuantity = e.PurchaseQuantity, PurchasePrice = e.PurchasePrice, Capacity = e.Capacity,
            UsesPerItem = e.UsesPerItem, PhotoUrl = e.PhotoUrl, CreatedAt = now, UpdatedAt = now, Version = Guid.NewGuid(),
        }));
        await db.SaveChangesAsync(ct);
        return missing.Count;
    }

    /// <summary>The catalog entry with this exact name (for sample recipes).</summary>
    public static Entry Get(CategoryKey category, string name) =>
        Entries.Single(e => e.Category == category && e.Name == name);

    private static IReadOnlyList<Entry> Load()
    {
        using var stream = typeof(CatalogSeed).Assembly.GetManifestResourceStream("SoapAndSoul.Data.Catalog.aromasoap.json")
            ?? throw new InvalidOperationException("Embedded catalog aromasoap.json is missing.");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        return JsonSerializer.Deserialize<List<Entry>>(stream, options) ?? [];
    }

    /// <summary>Name-based (version 5) UUID of the product URL.</summary>
    private static Guid IdFor(string source)
    {
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes("aromasoap:" + source));
        var bytes = hash.AsSpan(0, 16).ToArray();
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }
}
