using SoapAndSoul.Domain.Catalog;

namespace SoapAndSoul.Domain.Selection;

public interface ISelectionRule
{
    void Apply(SelectionContext ctx);
}

/// <summary>Mold, bottle and bases: a new pick replaces the previous one.</summary>
public sealed class SingleInCategoryRule : ISelectionRule
{
    public void Apply(SelectionContext ctx)
    {
        if (ctx.Category.Single) ctx.RemoveCategory(ctx.Category.Key);
    }
}

/// <summary>Essential oils and fragrance oils do not mix in soap: picking one removes the other.</summary>
public sealed class ExclusiveAromaRule : ISelectionRule
{
    public void Apply(SelectionContext ctx)
    {
        if (Categories.ExclusiveWith(ctx.Line, ctx.Category.Key) is not { } other) return;
        if (ctx.RemoveCategory(other) == 0) return;
        ctx.Notices.Add(other == CategoryKey.Fragrance ? "Запашки прибрано" : "Ефірні масла прибрано");
    }
}

/// <summary>A mold/bottle is one piece; its capacity becomes the recipe weight and the base amount.</summary>
public sealed class CapacityRule : ISelectionRule
{
    public void Apply(SelectionContext ctx)
    {
        if (!ctx.Category.HasCapacity) return;
        ctx.Amount ??= 1;
        if (ctx.Ingredient.Capacity is not { } capacity || capacity <= 0) return;
        ctx.Weight = capacity;
        foreach (var baseItem in ctx.ItemsIn(Categories.BaseOf(ctx.Line)).ToList())
            ctx.SetAmount(baseItem.IngredientId, capacity);
    }
}

/// <summary>The base fills the chosen mold/bottle; without one it falls back to its typical amount.</summary>
public sealed class BaseFollowsCapacityRule : ISelectionRule
{
    public void Apply(SelectionContext ctx)
    {
        if (ctx.Category.Key != Categories.BaseOf(ctx.Line) || ctx.Amount is not null) return;
        var capacity = ctx.Items
            .Select(i => ctx.Lookup(i.IngredientId))
            .FirstOrDefault(i => i is not null && Categories.Find(ctx.Line, i.Category)?.HasCapacity == true)
            ?.Capacity;
        if (capacity > 0) ctx.Amount = capacity;
    }
}

/// <summary>Everything else starts at the ingredient's typical portion.</summary>
public sealed class DefaultAmountRule : ISelectionRule
{
    public void Apply(SelectionContext ctx) => ctx.Amount ??= ctx.Ingredient.TypicalAmount;
}
