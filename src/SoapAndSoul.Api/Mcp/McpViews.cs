using SoapAndSoul.Api.Services;
using SoapAndSoul.Domain.Catalog;
using SoapAndSoul.Domain.Contracts;
using SoapAndSoul.Domain.Costing;
using ModelContextProtocol;

namespace SoapAndSoul.Api.Mcp;

// What the MCP tools return: the stored documents plus the derived facts (names, costs, missing slots)
// an assistant would otherwise have to compute itself.

public sealed record CategoryView(
    CategoryKey Key, string Label, string Hint, IReadOnlyList<MeasureUnit> Units,
    bool Required, bool Single, bool HasCapacity, bool Amortized);

/// <param name="UnitPrice">Price of one <see cref="Unit"/> in a recipe, UAH (molds already amortized).</param>
public sealed record IngredientView(
    Guid Id, CosmeticLine Line, CategoryKey Category, string Name, MeasureUnit Unit, decimal TypicalAmount,
    decimal PurchaseQuantity, decimal PurchasePrice, decimal? Capacity, int UsesPerItem, decimal UnitPrice);

public sealed record RecipeSummary(
    Guid Id, string Name, decimal Weight, int TimeMinutes, int BatchSize, int ItemCount, decimal CostPerPiece, DateTimeOffset? UpdatedAt);

public sealed record RecipeItemView(Guid IngredientId, string Name, CategoryKey Category, decimal Amount, MeasureUnit Unit, decimal Cost);

/// <param name="Weight">Weight of one piece, in <see cref="WeightUnit"/>.</param>
/// <param name="MissingRequired">Labels of required categories that have no ingredient yet.</param>
/// <param name="Notices">What the selection rules changed while applying the last edit.</param>
public sealed record RecipeView(
    Guid Id, CosmeticLine Line, string Name, string Description, decimal Weight, MeasureUnit WeightUnit,
    int TimeMinutes, int BatchSize, decimal CostPerPiece, decimal CostPerBatch,
    IReadOnlyList<RecipeItemView> Items, IReadOnlyList<string> MissingRequired, IReadOnlyList<string> Notices);

internal static class McpViews
{
    public static CategoryView ToView(this CategoryDefinition c) =>
        new(c.Key, c.Label, c.Hint, c.Units, c.Required, c.Single, c.HasCapacity, c.Amortized);

    public static IngredientView ToView(this IngredientDto i) =>
        new(i.Id, i.Line, i.Category, i.Name, i.Unit, i.TypicalAmount, i.PurchaseQuantity, i.PurchasePrice,
            i.Capacity, i.UsesPerItem, Money(CostCalculator.UnitPrice(i)));

    public static RecipeSummary ToSummary(this RecipeDto r, Func<Guid, IngredientDto?> lookup) =>
        new(r.Id, r.Name, r.Weight, r.TimeMinutes, r.BatchSize, r.Items.Count, Money(CostCalculator.RecipeCost(r, lookup)), r.UpdatedAt);

    public static RecipeView ToView(this RecipeDto r, Func<Guid, IngredientDto?> lookup, IReadOnlyList<string>? notices = null)
    {
        var items = r.Items
            .Select(item => (item, ing: lookup(item.IngredientId)))
            .Where(x => x.ing is not null)
            .Select(x => new RecipeItemView(x.ing!.Id, x.ing.Name, x.ing.Category, x.item.Amount, x.ing.Unit,
                Money(CostCalculator.ItemCost(x.ing, x.item.Amount))))
            .ToList();
        var missing = Categories.For(r.Line)
            .Where(c => c.Required && items.All(i => i.Category != c.Key))
            .Select(c => c.Label)
            .ToList();
        var cost = CostCalculator.RecipeCost(r, lookup);
        return new RecipeView(r.Id, r.Line, r.Name, r.Description, r.Weight, Categories.CapacityUnit(r.Line),
            r.TimeMinutes, r.BatchSize, Money(cost), Money(cost * r.BatchSize), items, missing, notices ?? []);
    }

    /// <summary>Unwraps a save, turning every failure into an error the assistant can read and act on.</summary>
    public static T Unwrap<T>(this SaveOutcome<T> outcome, string what) => outcome switch
    {
        SaveOutcome<T>.Saved { Value: var v } => v,
        SaveOutcome<T>.Invalid { Errors: var e } =>
            throw new McpException($"The {what} is invalid: " + string.Join(" ", e.Values.SelectMany(m => m))),
        SaveOutcome<T>.Conflict => throw new McpException($"The {what} was changed by someone else at the same time. Read it again and retry."),
        _ => throw new McpException($"The {what} was not found (it may have been deleted)."),
    };

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
