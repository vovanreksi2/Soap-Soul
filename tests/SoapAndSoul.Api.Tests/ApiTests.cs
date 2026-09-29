using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SoapAndSoul.Domain.Catalog;
using SoapAndSoul.Domain.Contracts;

namespace SoapAndSoul.Api.Tests;

public class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _http = factory.CreateClient();

    private static IngredientDto Mold() => new(Guid.CreateVersion7(), CosmeticLine.Soap, CategoryKey.Mold, "Форма",
        MeasureUnit.Piece, 1, 1, 180, 100, 100, null);

    private static IngredientDto Base() => new(Guid.CreateVersion7(), CosmeticLine.Soap, CategoryKey.SoapBase, "Основа",
        MeasureUnit.Gram, 100, 1000, 420, null, 1, null);

    private async Task<T> Put<T>(string url, T body)
    {
        var res = await _http.PutAsJsonAsync(url, body);
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<T>())!;
    }

    private Task<IngredientDto> Save(IngredientDto i) => Put($"/api/ingredients/{i.Id}", i);
    private Task<RecipeDto> Save(RecipeDto r) => Put($"/api/recipes/{r.Id}", r);

    private static RecipeDto NewRecipe(params RecipeItemDto[] items) =>
        new(Guid.CreateVersion7(), CosmeticLine.Soap, "Лавандове", "", 100, 40, 1, null, items);

    [Fact]
    public async Task Recipe_round_trips_with_items_in_order()
    {
        var mold = await Save(Mold());
        var soapBase = await Save(Base());

        var saved = await Save(NewRecipe(new(soapBase.Id, 100), new(mold.Id, 1)));
        var loaded = await _http.GetFromJsonAsync<RecipeDto>($"/api/recipes/{saved.Id}");

        Assert.NotNull(saved.Version);
        Assert.Equal([soapBase.Id, mold.Id], loaded!.Items.Select(i => i.IngredientId));
    }

    [Fact]
    public async Task Stale_version_is_rejected_with_the_current_copy()
    {
        var first = await Save(NewRecipe());
        var second = await Save(first with { Name = "Друга назва" });

        var res = await _http.PutAsJsonAsync($"/api/recipes/{first.Id}", first with { Name = "Застаріле" });

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var current = await res.Content.ReadFromJsonAsync<RecipeDto>();
        Assert.Equal("Друга назва", current!.Name);
        Assert.Equal(second.Version, current.Version);
    }

    [Fact]
    public async Task Invalid_recipe_returns_validation_problem()
    {
        var res = await _http.PutAsJsonAsync($"/api/recipes/{Guid.NewGuid()}", NewRecipe() with { Id = Guid.NewGuid(), Name = " " });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);

        var twoMolds = NewRecipe(new((await Save(Mold())).Id, 1), new((await Save(Mold())).Id, 1));
        res = await _http.PutAsJsonAsync($"/api/recipes/{twoMolds.Id}", twoMolds);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Deleting_an_ingredient_removes_it_from_recipes()
    {
        var mold = await Save(Mold());
        var soapBase = await Save(Base());
        var recipe = await Save(NewRecipe(new(mold.Id, 1), new(soapBase.Id, 100)));

        Assert.Equal(HttpStatusCode.NoContent, (await _http.DeleteAsync($"/api/ingredients/{soapBase.Id}")).StatusCode);

        var loaded = await _http.GetFromJsonAsync<RecipeDto>($"/api/recipes/{recipe.Id}");
        Assert.Equal([mold.Id], loaded!.Items.Select(i => i.IngredientId));
        Assert.NotEqual(recipe.Version, loaded.Version);
        var list = await _http.GetFromJsonAsync<List<IngredientDto>>("/api/ingredients?line=Soap");
        Assert.DoesNotContain(list!, i => i.Id == soapBase.Id);
    }

    [Fact]
    public async Task Deleted_recipe_disappears_from_list()
    {
        var recipe = await Save(NewRecipe());
        await _http.DeleteAsync($"/api/recipes/{recipe.Id}");

        var list = await _http.GetFromJsonAsync<List<RecipeDto>>("/api/recipes?line=Soap");
        Assert.DoesNotContain(list!, r => r.Id == recipe.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync($"/api/recipes/{recipe.Id}")).StatusCode);
    }

    [Fact]
    public async Task Image_upload_accepts_png_and_rejects_other_files()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
        var ok = await Upload(png, "image/png");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var url = (await ok.Content.ReadFromJsonAsync<ImageUploadResult>())!.Url;
        Assert.Equal(png, await _http.GetByteArrayAsync(url));

        var bad = await Upload("<script>"u8.ToArray(), "image/png");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Theory]
    [InlineData("/images/0199a0000000700080000000000000ff.png")]
    [InlineData("/images/appsettings.json")]
    [InlineData("/images/..%2Fappsettings.json")]
    public async Task Unknown_or_invalid_image_name_is_404(string url) =>
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync(url)).StatusCode);

    [Fact]
    public async Task Unknown_api_route_is_404_not_the_app_shell() =>
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync("/api/nope")).StatusCode);

    private Task<HttpResponseMessage> Upload(byte[] bytes, string type)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(type);
        return _http.PostAsync("/api/images", new MultipartFormDataContent { { content, "file", "photo" } });
    }
}
