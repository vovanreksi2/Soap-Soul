using SoapAndSoul.Domain.Catalog;
using SoapAndSoul.Domain.Contracts;

namespace SoapAndSoul.Domain.Selection;

/// <summary>Working state while an ingredient is being added to a recipe; rules mutate it in order.</summary>
public sealed class SelectionContext
{
    internal SelectionContext(
        CosmeticLine line, IngredientDto ingredient, List<RecipeItemDto> items, decimal weight, Func<Guid, IngredientDto?> lookup)
    {
        Line = line;
        Ingredient = ingredient;
        Category = Categories.Get(line, ingredient.Category);
        Items = items;
        Weight = weight;
        Lookup = lookup;
    }

    public CosmeticLine Line { get; }
    public IngredientDto Ingredient { get; }
    public CategoryDefinition Category { get; }
    public Func<Guid, IngredientDto?> Lookup { get; }

    /// <summary>The recipe's composition, without the ingredient being added.</summary>
    public List<RecipeItemDto> Items { get; }

    public decimal Weight { get; set; }

    /// <summary>Amount for the added ingredient; the first rule that sets it wins.</summary>
    public decimal? Amount { get; set; }

    public List<string> Notices { get; } = [];

    public IEnumerable<RecipeItemDto> ItemsIn(CategoryKey key) =>
        Items.Where(i => Lookup(i.IngredientId)?.Category == key);

    public int RemoveCategory(CategoryKey key) =>
        Items.RemoveAll(i => Lookup(i.IngredientId)?.Category == key);

    public void SetAmount(Guid ingredientId, decimal amount)
    {
        var index = Items.FindIndex(i => i.IngredientId == ingredientId);
        if (index >= 0) Items[index] = Items[index] with { Amount = amount };
    }
}

public sealed record SelectionResult(
    IReadOnlyList<RecipeItemDto> Items,
    decimal Weight,
    bool Added,
    IReadOnlyList<string> Notices)
{
    /// <summary>Single-choice categories (mold, base) close the picker after a pick.</summary>
    public bool ClosePicker { get; init; }
}
