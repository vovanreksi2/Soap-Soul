using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SoapAndSoul.Domain.Catalog;
using SoapAndSoul.Domain.Contracts;

namespace SoapAndSoul.Client.Services;

public sealed class HttpDataStore(HttpClient http) : IIngredientStore, IRecipeStore, IImageStore
{
    public async Task<IReadOnlyList<IngredientDto>> ListIngredientsAsync(CosmeticLine line, CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<IngredientDto>>($"api/ingredients?line={line}", ct) ?? [];

    public Task<SaveResult<IngredientDto>> SaveIngredientAsync(IngredientDto ingredient, CancellationToken ct = default) =>
        PutAsync($"api/ingredients/{ingredient.Id}", ingredient, ct);

    public async Task DeleteIngredientAsync(Guid id, CancellationToken ct = default) =>
        (await http.DeleteAsync($"api/ingredients/{id}", ct)).EnsureSuccessStatusCode();

    public async Task<IReadOnlyList<RecipeDto>> ListRecipesAsync(CosmeticLine line, CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<RecipeDto>>($"api/recipes?line={line}", ct) ?? [];

    public Task<SaveResult<RecipeDto>> SaveRecipeAsync(RecipeDto recipe, CancellationToken ct = default) =>
        PutAsync($"api/recipes/{recipe.Id}", recipe, ct);

    public async Task DeleteRecipeAsync(Guid id, CancellationToken ct = default) =>
        (await http.DeleteAsync($"api/recipes/{id}", ct)).EnsureSuccessStatusCode();

    public async Task<string> UploadAsync(Stream content, string contentType, CancellationToken ct = default)
    {
        using var file = new StreamContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        using var form = new MultipartFormDataContent { { file, "file", "photo" } };
        var response = await http.PostAsync("api/images", form, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ImageUploadResult>(ct))!.Url;
    }

    private async Task<SaveResult<T>> PutAsync<T>(string url, T body, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.PutAsJsonAsync(url, body, ct);
        }
        catch (HttpRequestException)
        {
            return new SaveResult<T>.Failed("Немає з’єднання з сервером.");
        }

        switch (response.StatusCode)
        {
            case HttpStatusCode.OK or HttpStatusCode.Created:
                return new SaveResult<T>.Saved((await response.Content.ReadFromJsonAsync<T>(ct))!);
            case HttpStatusCode.Conflict:
                return new SaveResult<T>.Conflict((await response.Content.ReadFromJsonAsync<T>(ct))!);
            case HttpStatusCode.NotFound:
                return new SaveResult<T>.Missing();
            case HttpStatusCode.BadRequest:
                var problem = await response.Content.ReadFromJsonAsync<ValidationProblem>(ct);
                return new SaveResult<T>.Invalid(problem?.Errors ?? new Dictionary<string, string[]>());
            default:
                return new SaveResult<T>.Failed($"Сервер відповів {(int)response.StatusCode}.");
        }
    }

    private sealed record ValidationProblem(Dictionary<string, string[]>? Errors);
}
