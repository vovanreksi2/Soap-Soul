using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SoapAndSoul.Api.Services;
using SoapAndSoul.Domain.Catalog;
using SoapAndSoul.Domain.Contracts;
using SoapAndSoul.Domain.Text;

namespace SoapAndSoul.Api.Mcp;

[McpServerToolType]
public sealed class IngredientTools(IngredientService ingredients)
{
    [McpServerTool(Name = "list_categories", ReadOnly = true, Idempotent = true)]
    [Description("Lists the fixed ingredient categories of a product line, in display order, with their rules: " +
                 "allowed units (the first is the default), whether the category is required in a recipe, " +
                 "single (at most one per recipe), hasCapacity (mold/bottle whose capacity sets the recipe weight) " +
                 "and amortized (price spread over usesPerItem recipes).")]
    public static IReadOnlyList<CategoryView> ListCategories(
        [Description("Product line: Soap or Perfume.")] CosmeticLine line) =>
        Categories.For(line).Select(c => c.ToView()).ToList();

    [McpServerTool(Name = "list_ingredients", ReadOnly = true, Idempotent = true)]
    [Description("Lists the ingredients of a product line with purchase data and unit price (UAH). " +
                 "The list includes the supplier catalog (reference entries with inStock false); " +
                 "optionally filtered by stock, category and a typo-tolerant name search.")]
    public async Task<IReadOnlyList<IngredientView>> ListIngredients(
        [Description("Product line: Soap or Perfume.")] CosmeticLine line,
        [Description("Only this category.")] CategoryKey? category = null,
        [Description("Part of the name, in Ukrainian.")] string? search = null,
        [Description("true: only what the user has in stock; false: only reference entries.")] bool? inStock = null,
        CancellationToken ct = default)
    {
        var all = await ingredients.ListAsync(line, ct);
        return all
            .Where(i => inStock is null || i.InStock == inStock)
            .Where(i => category is null || i.Category == category)
            .Where(i => FuzzyMatcher.Matches(search, i.Name))
            .Select(i => i.ToView())
            .ToList();
    }

    [McpServerTool(Name = "create_ingredient")]
    [Description("Adds an ingredient to the catalog. Prices are in UAH for purchaseQuantity units; " +
                 "drops are bought in millilitres (1 ml = 20 drops), so for a Drop ingredient give purchaseQuantity in ml.")]
    public async Task<IngredientView> CreateIngredient(
        [Description("Product line: Soap or Perfume.")] CosmeticLine line,
        [Description("Category key, see list_categories.")] CategoryKey category,
        [Description("Name in Ukrainian.")] string name,
        [Description("Price paid for purchaseQuantity, UAH.")] decimal purchasePrice,
        [Description("How much was bought (ml for drops, pieces for molds and bottles).")] decimal purchaseQuantity,
        [Description("Unit used in recipes; defaults to the category's first unit.")] MeasureUnit? unit = null,
        [Description("Default amount added to a recipe, in unit. Required except for molds and bottles.")] decimal? typicalAmount = null,
        [Description("Mold weight (g) or bottle volume (ml). Required for categories with capacity.")] decimal? capacity = null,
        [Description("How many recipes one purchased item serves (molds are reused; default 100 for molds, 1 otherwise).")] int? usesPerItem = null,
        [Description("Whether the user has it in stock (default true).")] bool inStock = true,
        CancellationToken ct = default)
    {
        var cat = Categories.Find(line, category)
            ?? throw new McpException($"Category {category} does not exist in line {line}. Use list_categories.");
        var dto = new IngredientDto(
            Guid.CreateVersion7(), line, category, name, unit ?? cat.DefaultUnit,
            typicalAmount ?? (cat.HasCapacity ? 1 : 0), purchaseQuantity, purchasePrice,
            cat.HasCapacity ? capacity : null,
            usesPerItem ?? (cat.Amortized ? Categories.DefaultMoldUses : 1),
            PhotoUrl: null,
            InStock: inStock);
        return (await ingredients.SaveAsync(dto, ct)).Unwrap("ingredient").ToView();
    }

    [McpServerTool(Name = "update_ingredient", Idempotent = true)]
    [Description("Changes fields of an ingredient; omitted fields stay as they are. Line and category cannot change.")]
    public async Task<IngredientView> UpdateIngredient(
        [Description("Ingredient id.")] Guid id,
        [Description("New name.")] string? name = null,
        [Description("Price paid for purchaseQuantity, UAH.")] decimal? purchasePrice = null,
        [Description("How much was bought.")] decimal? purchaseQuantity = null,
        [Description("Unit used in recipes.")] MeasureUnit? unit = null,
        [Description("Default amount added to a recipe.")] decimal? typicalAmount = null,
        [Description("Mold weight (g) or bottle volume (ml).")] decimal? capacity = null,
        [Description("How many recipes one purchased item serves.")] int? usesPerItem = null,
        [Description("Whether the user has it in stock.")] bool? inStock = null,
        CancellationToken ct = default)
    {
        var current = await ingredients.GetAsync(id, ct) ?? throw new McpException($"Ingredient {id} not found.");
        var dto = current with
        {
            Name = name ?? current.Name,
            PurchasePrice = purchasePrice ?? current.PurchasePrice,
            PurchaseQuantity = purchaseQuantity ?? current.PurchaseQuantity,
            Unit = unit ?? current.Unit,
            TypicalAmount = typicalAmount ?? current.TypicalAmount,
            Capacity = capacity ?? current.Capacity,
            UsesPerItem = usesPerItem ?? current.UsesPerItem,
            InStock = inStock ?? current.InStock,
        };
        return (await ingredients.SaveAsync(dto, ct)).Unwrap("ingredient").ToView();
    }

    [McpServerTool(Name = "delete_ingredient", Destructive = true, Idempotent = true)]
    [Description("Deletes an ingredient and removes it from every recipe that uses it.")]
    public async Task<string> DeleteIngredient([Description("Ingredient id.")] Guid id, CancellationToken ct = default) =>
        await ingredients.DeleteAsync(id, ct) ? "Deleted." : "Nothing to delete: the ingredient does not exist.";
}
