using SoapAndSoul.Domain.Catalog;
using SoapAndSoul.Domain.Contracts;

namespace SoapAndSoul.Domain.Selection;

/// <summary>Adds and removes ingredients in a recipe by running the selection rules in order.</summary>
public sealed class SelectionEngine(IReadOnlyList<ISelectionRule> rules)
{
    public static SelectionEngine Default { get; } = new(
    [
        new SingleInCategoryRule(),
        new ExclusiveAromaRule(),
        new CapacityRule(),
        new BaseFollowsCapacityRule(),
        new DefaultAmountRule(),
    ]);

    /// <summary>Removes the ingredient if it is in the recipe, otherwise adds it by the rules.</summary>
    public SelectionResult Toggle(
        CosmeticLine line, IReadOnlyList<RecipeItemDto> items, decimal weight, IngredientDto ingredient, Func<Guid, IngredientDto?> lookup)
    {
        if (items.Any(i => i.IngredientId == ingredient.Id))
            return new SelectionResult(Remove(items, ingredient.Id), weight, Added: false, []);

        var ctx = new SelectionContext(line, ingredient, [.. items], weight, lookup);
        foreach (var rule in rules) rule.Apply(ctx);
        ctx.Items.Add(new RecipeItemDto(ingredient.Id, ctx.Amount ?? 0));
        return new SelectionResult(ctx.Items, ctx.Weight, Added: true, ctx.Notices)
        {
            ClosePicker = ctx.Category.Single,
        };
    }

    public static IReadOnlyList<RecipeItemDto> Remove(IReadOnlyList<RecipeItemDto> items, Guid ingredientId) =>
        items.Where(i => i.IngredientId != ingredientId).ToList();

    public static IReadOnlyList<RecipeItemDto> SetAmount(IReadOnlyList<RecipeItemDto> items, Guid ingredientId, decimal amount) =>
        items.Select(i => i.IngredientId == ingredientId ? i with { Amount = Math.Max(0, amount) } : i).ToList();
}
