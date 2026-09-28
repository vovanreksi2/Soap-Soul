using SoapAndSoul.Client.Services;
using SoapAndSoul.Domain.Catalog;
using SoapAndSoul.Domain.Contracts;

namespace SoapAndSoul.Client.State;

/// <summary>In-memory copy of ingredients and recipes per line, shared by all screens.</summary>
public sealed class CatalogState(IIngredientStore ingredients, IRecipeStore recipes)
{
    private sealed class LineData
    {
        public List<IngredientDto> Ingredients { get; set; } = [];
        public List<RecipeDto> Recipes { get; set; } = [];
        public Task? Loading { get; set; }
    }

    private readonly Dictionary<CosmeticLine, LineData> _lines = Lines.All.ToDictionary(l => l, _ => new LineData());

    public event Action? Changed;

    public Task EnsureLoadedAsync(CosmeticLine line, bool force = false)
    {
        var data = _lines[line];
        if (force || data.Loading is null || data.Loading.IsFaulted) data.Loading = LoadAsync(line, data);
        return data.Loading;
    }

    private async Task LoadAsync(CosmeticLine line, LineData data)
    {
        var i = ingredients.ListIngredientsAsync(line);
        var r = recipes.ListRecipesAsync(line);
        data.Ingredients = [.. await i];
        data.Recipes = [.. await r];
        Changed?.Invoke();
    }

    public IReadOnlyList<IngredientDto> Ingredients(CosmeticLine line) => _lines[line].Ingredients;
    public IReadOnlyList<RecipeDto> Recipes(CosmeticLine line) => _lines[line].Recipes;

    public IngredientDto? Ingredient(Guid id) =>
        _lines.Values.SelectMany(l => l.Ingredients).FirstOrDefault(i => i.Id == id);

    public RecipeDto? Recipe(CosmeticLine line, Guid id) => _lines[line].Recipes.FirstOrDefault(r => r.Id == id);

    public Func<Guid, IngredientDto?> Lookup => Ingredient;

    public async Task<SaveResult<IngredientDto>> SaveIngredientAsync(IngredientDto dto)
    {
        var result = await ingredients.SaveIngredientAsync(dto);
        if (Returned(result) is { } saved) Replace(_lines[dto.Line].Ingredients, saved, i => i.Id);
        return result;
    }

    /// <summary>Deletes the ingredient; the server also removes it from recipes, so they are reloaded.</summary>
    public async Task DeleteIngredientAsync(IngredientDto dto)
    {
        await ingredients.DeleteIngredientAsync(dto.Id);
        var data = _lines[dto.Line];
        data.Ingredients.RemoveAll(i => i.Id == dto.Id);
        data.Recipes = [.. await recipes.ListRecipesAsync(dto.Line)];
        Changed?.Invoke();
    }

    public async Task<SaveResult<RecipeDto>> SaveRecipeAsync(RecipeDto dto)
    {
        var result = await recipes.SaveRecipeAsync(dto);
        if (Returned(result) is { } saved) Replace(_lines[dto.Line].Recipes, saved, r => r.Id);
        else if (result is SaveResult<RecipeDto>.Missing)
        {
            _lines[dto.Line].Recipes.RemoveAll(r => r.Id == dto.Id);
            Changed?.Invoke();
        }
        return result;
    }

    public async Task DeleteRecipeAsync(RecipeDto dto)
    {
        await recipes.DeleteRecipeAsync(dto.Id);
        _lines[dto.Line].Recipes.RemoveAll(r => r.Id == dto.Id);
        Changed?.Invoke();
    }

    /// <summary>The server's copy after a save: the saved value, or the current one on a conflict.</summary>
    private static T? Returned<T>(SaveResult<T> result) where T : class => result switch
    {
        SaveResult<T>.Saved s => s.Value,
        SaveResult<T>.Conflict c => c.Current,
        _ => null,
    };

    private void Replace<T>(List<T> list, T item, Func<T, Guid> id)
    {
        var index = list.FindIndex(x => id(x) == id(item));
        if (index >= 0) list[index] = item;
        else list.Add(item);
        Changed?.Invoke();
    }
}
