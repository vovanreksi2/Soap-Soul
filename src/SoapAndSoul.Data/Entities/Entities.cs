using SoapAndSoul.Domain.Catalog;

namespace SoapAndSoul.Data.Entities;

/// <summary>
/// Common columns. Ids are client-generated Guids and <see cref="Version"/> is an
/// optimistic-concurrency token, so records can later be created offline and synced.
/// Deletes are soft (tombstones), which a future sync also needs.
/// </summary>
public abstract class TrackedEntity
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid Version { get; set; }
    public bool IsDeleted { get; set; }
}

public class Ingredient : TrackedEntity
{
    public CosmeticLine Line { get; set; }
    public CategoryKey Category { get; set; }
    public string Name { get; set; } = "";
    public MeasureUnit Unit { get; set; }
    public decimal TypicalAmount { get; set; }
    public decimal PurchaseQuantity { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal? Capacity { get; set; }
    public int UsesPerItem { get; set; } = 1;
    public string? PhotoUrl { get; set; }
    /// <summary>False for supplier-catalog entries the user only keeps for reference.</summary>
    public bool InStock { get; set; } = true;
}

public class Recipe : TrackedEntity
{
    public CosmeticLine Line { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Weight { get; set; }
    public int TimeMinutes { get; set; }
    public int BatchSize { get; set; } = 1;
    public string? PhotoUrl { get; set; }
    public List<RecipeItem> Items { get; set; } = [];
}

public class RecipeItem
{
    public Guid RecipeId { get; set; }
    public Guid IngredientId { get; set; }
    public decimal Amount { get; set; }
    /// <summary>Keeps the order the user added items in.</summary>
    public int Position { get; set; }

    public Recipe Recipe { get; set; } = null!;
    public Ingredient Ingredient { get; set; } = null!;
}
