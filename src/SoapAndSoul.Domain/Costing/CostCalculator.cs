using SoapAndSoul.Domain.Catalog;
using SoapAndSoul.Domain.Contracts;

namespace SoapAndSoul.Domain.Costing;

public static class CostCalculator
{
    /// <summary>
    /// Price of one unit of <see cref="IngredientDto.Unit"/> as used in a recipe.
    /// Drops are bought in millilitres (1 мл = 20 крап); reusable items (molds) are
    /// spread over <see cref="IngredientDto.UsesPerItem"/> recipes.
    /// </summary>
    public static decimal UnitPrice(MeasureUnit unit, decimal purchaseQuantity, decimal purchasePrice, int usesPerItem = 1)
    {
        if (purchaseQuantity <= 0 || purchasePrice <= 0) return 0;
        var quantity = unit == MeasureUnit.Drop ? purchaseQuantity * Units.DropsPerMilliliter : purchaseQuantity;
        return purchasePrice / quantity / Math.Max(1, usesPerItem);
    }

    public static decimal UnitPrice(IngredientDto i) =>
        UnitPrice(i.Unit, i.PurchaseQuantity, i.PurchasePrice, i.UsesPerItem);

    public static decimal ItemCost(IngredientDto ingredient, decimal amount) => UnitPrice(ingredient) * amount;

    /// <summary>Cost of one piece. Items whose ingredient no longer exists are ignored.</summary>
    public static decimal RecipeCost(IEnumerable<RecipeItemDto> items, Func<Guid, IngredientDto?> lookup) =>
        items.Sum(item => lookup(item.IngredientId) is { } i ? ItemCost(i, item.Amount) : 0);

    public static decimal RecipeCost(RecipeDto recipe, Func<Guid, IngredientDto?> lookup) =>
        RecipeCost(recipe.Items, lookup);
}
