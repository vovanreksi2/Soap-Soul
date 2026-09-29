using SoapAndSoul.Domain.Catalog;
using SoapAndSoul.Domain.Contracts;
using SoapAndSoul.Domain.Selection;

namespace SoapAndSoul.Domain.Drafts;

/// <param name="Unmatched">Spoken names that matched no catalog ingredient of the recipe's line.</param>
public sealed record DraftApplication(RecipeDto Recipe, IReadOnlyList<string> Notices, IReadOnlyList<string> Unmatched);

/// <summary>
/// Merges a dictated draft into a recipe. Ingredients go through the selection rules exactly as if
/// they were picked by hand (one mold, no fragrance with essential oils, base follows the mold), so
/// dictation can be repeated to add more to the same recipe.
/// </summary>
public static class RecipeDraftApplier
{
    public static DraftApplication Apply(RecipeDto recipe, RecipeDraftDto draft, Func<Guid, IngredientDto?> lookup) =>
        Apply(recipe, draft, lookup, SelectionEngine.Default);

    public static DraftApplication Apply(
        RecipeDto recipe, RecipeDraftDto draft, Func<Guid, IngredientDto?> lookup, SelectionEngine engine)
    {
        var notices = new List<string>();
        var unmatched = new List<string>();
        var items = recipe.Items;
        var weight = draft.Weight is > 0 and var w ? w : recipe.Weight;

        foreach (var d in draft.Items)
        {
            if (d.IngredientId is not { } id || lookup(id) is not { } ing || ing.Line != recipe.Line)
            {
                unmatched.Add(d.SpokenName);
                continue;
            }
            if (!items.Any(i => i.IngredientId == id))
            {
                var added = engine.Toggle(recipe.Line, items, weight, ing, lookup);
                items = added.Items;
                weight = added.Weight;
                notices.AddRange(added.Notices);
            }

            // Molds and bottles are one piece; their capacity already set the weight.
            if (d.Amount is not { } amount || Categories.Get(recipe.Line, ing.Category).HasCapacity) continue;
            var unit = d.Unit ?? ing.Unit;
            if (Units.Convert(amount, unit, ing.Unit) is { } converted)
                items = SelectionEngine.SetAmount(items, id, converted);
            else
                notices.Add($"«{ing.Name}»: {unit.Label()} не переводяться в {ing.Unit.Label()}, лишено типову порцію");
        }

        var updated = recipe with
        {
            Name = string.IsNullOrWhiteSpace(draft.Name) ? recipe.Name : draft.Name.Trim(),
            Description = MergeDescription(recipe.Description, draft.Description),
            Weight = weight,
            TimeMinutes = draft.TimeMinutes is >= 0 and var t ? t : recipe.TimeMinutes,
            BatchSize = draft.BatchSize is >= 1 and var b ? b : recipe.BatchSize,
            Items = items,
        };
        return new DraftApplication(updated, notices, unmatched);
    }

    /// <summary>Dictation adds to the description instead of replacing what was written before.</summary>
    private static string MergeDescription(string current, string? dictated)
    {
        if (string.IsNullOrWhiteSpace(dictated)) return current;
        return string.IsNullOrWhiteSpace(current) ? dictated.Trim() : current.TrimEnd() + "\n" + dictated.Trim();
    }
}
