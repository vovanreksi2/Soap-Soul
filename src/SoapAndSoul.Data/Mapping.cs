using SoapAndSoul.Data.Entities;
using SoapAndSoul.Domain.Contracts;

namespace SoapAndSoul.Data;

public static class Mapping
{
    public static IngredientDto ToDto(this Ingredient e) => new(
        e.Id, e.Line, e.Category, e.Name, e.Unit, e.TypicalAmount, e.PurchaseQuantity, e.PurchasePrice,
        e.Capacity, e.UsesPerItem, e.PhotoUrl, e.InStock, e.Version);

    public static void Apply(this Ingredient e, IngredientDto d)
    {
        e.Line = d.Line;
        e.Category = d.Category;
        e.Name = d.Name.Trim();
        e.Unit = d.Unit;
        e.TypicalAmount = d.TypicalAmount;
        e.PurchaseQuantity = d.PurchaseQuantity;
        e.PurchasePrice = d.PurchasePrice;
        e.Capacity = d.Capacity;
        e.UsesPerItem = d.UsesPerItem;
        e.PhotoUrl = d.PhotoUrl;
        e.InStock = d.InStock;
    }

    public static RecipeDto ToDto(this Recipe e) => new(
        e.Id, e.Line, e.Name, e.Description, e.Weight, e.TimeMinutes, e.BatchSize, e.PhotoUrl,
        e.Items.OrderBy(i => i.Position).Select(i => new RecipeItemDto(i.IngredientId, i.Amount)).ToList(),
        e.Version, e.UpdatedAt);

    /// <summary>Copies scalar fields and replaces the composition with the document's items.</summary>
    public static void Apply(this Recipe e, RecipeDto d)
    {
        e.Name = d.Name.Trim();
        e.Description = d.Description;
        e.Weight = d.Weight;
        e.TimeMinutes = d.TimeMinutes;
        e.BatchSize = d.BatchSize;
        e.PhotoUrl = d.PhotoUrl;

        var wanted = d.Items.Select((item, position) => (item, position)).ToDictionary(x => x.item.IngredientId);
        e.Items.RemoveAll(i => !wanted.ContainsKey(i.IngredientId));
        foreach (var (item, position) in wanted.Values)
        {
            var existing = e.Items.FirstOrDefault(i => i.IngredientId == item.IngredientId);
            if (existing is null)
                e.Items.Add(new RecipeItem { RecipeId = e.Id, IngredientId = item.IngredientId, Amount = item.Amount, Position = position });
            else
            {
                existing.Amount = item.Amount;
                existing.Position = position;
            }
        }
    }
}
