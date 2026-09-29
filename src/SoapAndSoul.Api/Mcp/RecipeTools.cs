using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SoapAndSoul.Api.Services;
using SoapAndSoul.Domain.Catalog;
using SoapAndSoul.Domain.Contracts;
using SoapAndSoul.Domain.Selection;
using SoapAndSoul.Domain.Text;

namespace SoapAndSoul.Api.Mcp;

/// <summary>
/// Recipe tools. Each edit reads the current recipe, changes it and saves the whole document, so the
/// same validation and selection rules apply as in the app.
/// </summary>
[McpServerToolType]
public sealed class RecipeTools(RecipeService recipes, IngredientService ingredients)
{
    [McpServerTool(Name = "list_recipes", ReadOnly = true, Idempotent = true)]
    [Description("Lists the recipes of a product line with their cost per piece (UAH). Optional typo-tolerant name search.")]
    public async Task<IReadOnlyList<RecipeSummary>> ListRecipes(
        [Description("Product line: Soap or Perfume.")] CosmeticLine line,
        [Description("Part of the name, in Ukrainian.")] string? search = null,
        CancellationToken ct = default)
    {
        var lookup = await LookupAsync(line, ct);
        var all = await recipes.ListAsync(line, ct);
        return all
            .Where(r => FuzzyMatcher.Matches(search, r.Name))
            .OrderBy(r => r.Name, UkrainianComparer.Instance)
            .Select(r => r.ToSummary(lookup))
            .ToList();
    }

    [McpServerTool(Name = "get_recipe", ReadOnly = true, Idempotent = true)]
    [Description("Returns a recipe with its composition, item costs, cost per piece and per batch, " +
                 "and the required categories that are still missing.")]
    public async Task<RecipeView> GetRecipe([Description("Recipe id.")] Guid id, CancellationToken ct = default)
    {
        var recipe = await LoadAsync(id, ct);
        return recipe.ToView(await LookupAsync(recipe.Line, ct));
    }

    [McpServerTool(Name = "create_recipe")]
    [Description("Creates an empty recipe. Add ingredients with add_recipe_ingredient.")]
    public async Task<RecipeView> CreateRecipe(
        [Description("Product line: Soap or Perfume.")] CosmeticLine line,
        [Description("Name in Ukrainian.")] string name,
        [Description("Preparation steps or notes.")] string? description = null,
        [Description("Preparation time, minutes.")] int? timeMinutes = null,
        [Description("Pieces made at once (default 1).")] int? batchSize = null,
        [Description("Weight of one piece (g for soap, ml for perfume); a mold or bottle sets it automatically.")] decimal? weight = null,
        CancellationToken ct = default)
    {
        var dto = new RecipeDto(Guid.CreateVersion7(), line, name, description ?? "", weight ?? 0, timeMinutes ?? 0,
            batchSize ?? 1, PhotoUrl: null, Items: []);
        var saved = (await recipes.SaveAsync(dto, ct)).Unwrap("recipe");
        return saved.ToView(await LookupAsync(line, ct));
    }

    [McpServerTool(Name = "update_recipe", Idempotent = true)]
    [Description("Changes recipe fields; omitted fields stay as they are.")]
    public async Task<RecipeView> UpdateRecipe(
        [Description("Recipe id.")] Guid id,
        [Description("New name.")] string? name = null,
        [Description("New description (replaces the old one).")] string? description = null,
        [Description("Weight of one piece.")] decimal? weight = null,
        [Description("Preparation time, minutes.")] int? timeMinutes = null,
        [Description("Pieces made at once.")] int? batchSize = null,
        CancellationToken ct = default) =>
        await EditAsync(id, (r, _) => (r with
        {
            Name = name ?? r.Name,
            Description = description ?? r.Description,
            Weight = weight ?? r.Weight,
            TimeMinutes = timeMinutes ?? r.TimeMinutes,
            BatchSize = batchSize ?? r.BatchSize,
        }, []), ct);

    [McpServerTool(Name = "add_recipe_ingredient")]
    [Description("Adds a catalog ingredient to a recipe using the app's rules: a new mold, bottle or base replaces the old one, " +
                 "the mold/bottle capacity becomes the recipe weight and the base amount, and in soap fragrances and essential " +
                 "oils exclude each other. The amount defaults to the ingredient's typical portion. " +
                 "If the ingredient is already in the recipe, only its amount is changed. The result lists what the rules changed.")]
    public async Task<RecipeView> AddRecipeIngredient(
        [Description("Recipe id.")] Guid recipeId,
        [Description("Ingredient id from list_ingredients (same line as the recipe).")] Guid ingredientId,
        [Description("Amount in the ingredient's unit; ignored for molds and bottles.")] decimal? amount = null,
        CancellationToken ct = default)
    {
        var ingredient = await ingredients.GetAsync(ingredientId, ct) ?? throw new McpException($"Ingredient {ingredientId} not found.");
        return await EditAsync(recipeId, (r, lookup) =>
        {
            if (ingredient.Line != r.Line) throw new McpException($"Ingredient «{ingredient.Name}» belongs to the {ingredient.Line} line.");
            var items = r.Items;
            var weight = r.Weight;
            IReadOnlyList<string> notices = [];
            if (items.All(i => i.IngredientId != ingredientId))
            {
                var added = SelectionEngine.Default.Toggle(r.Line, items, weight, ingredient, lookup);
                (items, weight, notices) = (added.Items, added.Weight, added.Notices);
            }
            if (amount is { } a && !Categories.Get(r.Line, ingredient.Category).HasCapacity)
                items = SelectionEngine.SetAmount(items, ingredientId, a);
            return (r with { Items = items, Weight = weight }, notices);
        }, ct);
    }

    [McpServerTool(Name = "set_recipe_ingredient_amount", Idempotent = true)]
    [Description("Sets the amount of an ingredient that is already in the recipe, in the ingredient's unit.")]
    public Task<RecipeView> SetRecipeIngredientAmount(
        [Description("Recipe id.")] Guid recipeId,
        [Description("Ingredient id.")] Guid ingredientId,
        [Description("New amount, not negative.")] decimal amount,
        CancellationToken ct = default) =>
        EditAsync(recipeId, (r, _) =>
        {
            if (r.Items.All(i => i.IngredientId != ingredientId)) throw new McpException("The ingredient is not in this recipe.");
            return (r with { Items = SelectionEngine.SetAmount(r.Items, ingredientId, amount) }, []);
        }, ct);

    [McpServerTool(Name = "remove_recipe_ingredient", Idempotent = true)]
    [Description("Removes an ingredient from a recipe (the ingredient stays in the catalog).")]
    public Task<RecipeView> RemoveRecipeIngredient(
        [Description("Recipe id.")] Guid recipeId,
        [Description("Ingredient id.")] Guid ingredientId,
        CancellationToken ct = default) =>
        EditAsync(recipeId, (r, _) => (r with { Items = SelectionEngine.Remove(r.Items, ingredientId) }, []), ct);

    [McpServerTool(Name = "delete_recipe", Destructive = true, Idempotent = true)]
    [Description("Deletes a recipe.")]
    public async Task<string> DeleteRecipe([Description("Recipe id.")] Guid id, CancellationToken ct = default) =>
        await recipes.DeleteAsync(id, ct) ? "Deleted." : "Nothing to delete: the recipe does not exist.";

    private async Task<RecipeView> EditAsync(
        Guid id, Func<RecipeDto, Func<Guid, IngredientDto?>, (RecipeDto Recipe, IReadOnlyList<string> Notices)> change,
        CancellationToken ct)
    {
        var current = await LoadAsync(id, ct);
        var lookup = await LookupAsync(current.Line, ct);
        var (updated, notices) = change(current, lookup);
        var saved = (await recipes.SaveAsync(updated, ct)).Unwrap("recipe");
        return saved.ToView(lookup, notices);
    }

    private async Task<RecipeDto> LoadAsync(Guid id, CancellationToken ct) =>
        await recipes.GetAsync(id, ct) ?? throw new McpException($"Recipe {id} not found.");

    private async Task<Func<Guid, IngredientDto?>> LookupAsync(CosmeticLine line, CancellationToken ct)
    {
        var byId = (await ingredients.ListAsync(line, ct)).ToDictionary(i => i.Id);
        return byId.GetValueOrDefault;
    }
}
