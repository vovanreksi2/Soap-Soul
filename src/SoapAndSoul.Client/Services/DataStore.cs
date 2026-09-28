using SoapAndSoul.Domain.Catalog;
using SoapAndSoul.Domain.Contracts;

namespace SoapAndSoul.Client.Services;

/// <summary>Outcome of a save; the API answers stale versions with the current copy.</summary>
public abstract record SaveResult<T>
{
    public sealed record Saved(T Value) : SaveResult<T>;
    public sealed record Conflict(T Current) : SaveResult<T>;
    public sealed record Missing : SaveResult<T>;
    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : SaveResult<T>;
    public sealed record Failed(string Message) : SaveResult<T>;
}

/// <summary>
/// Where the app reads and writes data. Today it is the HTTP API (<see cref="HttpDataStore"/>);
/// an offline store (IndexedDB + sync queue) can implement the same interfaces later.
/// </summary>
public interface IIngredientStore
{
    Task<IReadOnlyList<IngredientDto>> ListIngredientsAsync(CosmeticLine line, CancellationToken ct = default);
    Task<SaveResult<IngredientDto>> SaveIngredientAsync(IngredientDto ingredient, CancellationToken ct = default);
    Task DeleteIngredientAsync(Guid id, CancellationToken ct = default);
}

public interface IRecipeStore
{
    Task<IReadOnlyList<RecipeDto>> ListRecipesAsync(CosmeticLine line, CancellationToken ct = default);
    Task<SaveResult<RecipeDto>> SaveRecipeAsync(RecipeDto recipe, CancellationToken ct = default);
    Task DeleteRecipeAsync(Guid id, CancellationToken ct = default);
}

public interface IImageStore
{
    /// <returns>URL of the stored image.</returns>
    Task<string> UploadAsync(Stream content, string contentType, CancellationToken ct = default);
}
