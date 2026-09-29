using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SoapAndSoul.Domain.Catalog;
using SoapAndSoul.Domain.Contracts;
using static SoapAndSoul.Api.Tests.TestCatalog;

namespace SoapAndSoul.Api.Tests;

public class RecipeApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _http = factory.CreateClient();

    private async Task<RecipeDto> GetAsync(Guid id) => (await _http.GetFromJsonAsync<RecipeDto>($"/api/recipes/{id}"))!;

    private async Task<string[]> ItemErrorsAsync(HttpResponseMessage res)
    {
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors["Items"];
    }

    [Fact]
    public async Task Saving_replaces_the_whole_composition()
    {
        var mold = await _http.SaveAsync(Mold());
        var soapBase = await _http.SaveAsync(SoapBase());
        var pigment = await _http.SaveAsync(Pigment());
        var oil = await _http.SaveAsync(EssentialOil());
        var saved = await _http.SaveAsync(Recipe(Unique("Склад"), new(mold.Id, 1), new(soapBase.Id, 100), new(pigment.Id, 3)));

        await _http.SaveAsync(saved with { Items = [new(oil.Id, 12), new(soapBase.Id, 90), new(mold.Id, 1)] });

        var loaded = await GetAsync(saved.Id);
        Assert.Equal([new(oil.Id, 12), new(soapBase.Id, 90), new(mold.Id, 1)], loaded.Items);
        var rows = await factory.WithDbAsync(db => db.RecipeItems.CountAsync(i => i.RecipeId == saved.Id));
        Assert.Equal(3, rows);
    }

    [Fact]
    public async Task Metadata_is_saved_and_the_name_trimmed()
    {
        var recipe = Recipe("  " + Unique("Мигдаль") + "  ") with
        {
            Description = "Для сухої шкіри", Weight = 120, TimeMinutes = 45, BatchSize = 6, PhotoUrl = "/images/x.png",
        };

        var created = await _http.PutAsJsonAsync($"/api/recipes/{recipe.Id}", recipe);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var loaded = await GetAsync(recipe.Id);
        Assert.Equal(recipe with { Name = recipe.Name.Trim(), Version = loaded.Version, UpdatedAt = loaded.UpdatedAt }, loaded with { Items = [] });
        Assert.NotNull(loaded.UpdatedAt);
    }

    [Fact]
    public async Task List_contains_only_the_requested_line()
    {
        var soap = await _http.SaveAsync(Recipe(Unique("Мило")));
        var perfume = await _http.SaveAsync(Recipe(CosmeticLine.Perfume, Unique("Парфум")));

        var soapList = await _http.GetFromJsonAsync<List<RecipeDto>>("/api/recipes?line=Soap");
        var perfumeList = await _http.GetFromJsonAsync<List<RecipeDto>>("/api/recipes?line=Perfume");

        Assert.Contains(soapList!, r => r.Id == soap.Id);
        Assert.DoesNotContain(soapList!, r => r.Id == perfume.Id);
        Assert.Contains(perfumeList!, r => r.Id == perfume.Id);
        Assert.DoesNotContain(perfumeList!, r => r.Id == soap.Id);
    }

    [Fact]
    public async Task Ingredient_from_another_line_is_rejected()
    {
        var bottle = await _http.SaveAsync(Bottle());
        var recipe = Recipe(Unique("Чуже"), new RecipeItemDto(bottle.Id, 1));

        var errors = await ItemErrorsAsync(await _http.PutAsJsonAsync($"/api/recipes/{recipe.Id}", recipe));

        Assert.Contains(errors, e => e.Contains("з іншої лінійки"));
    }

    [Fact]
    public async Task Fragrance_and_essential_oil_cannot_be_combined()
    {
        var fragrance = await _http.SaveAsync(Fragrance());
        var oil = await _http.SaveAsync(EssentialOil());
        var recipe = Recipe(Unique("Змішане"), new(fragrance.Id, 10), new(oil.Id, 10));

        var errors = await ItemErrorsAsync(await _http.PutAsJsonAsync($"/api/recipes/{recipe.Id}", recipe));

        Assert.Contains("Запашки й ефірні масла не поєднуються.", errors);
    }

    [Fact]
    public async Task Deleted_or_unknown_ingredient_cannot_be_added()
    {
        var pigment = await _http.SaveAsync(Pigment());
        await _http.DeleteAsync($"/api/ingredients/{pigment.Id}");
        var recipe = Recipe(Unique("Без кольору"), new RecipeItemDto(pigment.Id, 3));

        var errors = await ItemErrorsAsync(await _http.PutAsJsonAsync($"/api/recipes/{recipe.Id}", recipe));

        Assert.Contains($"Інгредієнт {pigment.Id} не знайдено.", errors);
    }

    [Fact]
    public async Task Line_of_a_saved_recipe_cannot_change()
    {
        var saved = await _http.SaveAsync(Recipe(Unique("Мило")));

        var res = await _http.PutAsJsonAsync($"/api/recipes/{saved.Id}", saved with { Line = CosmeticLine.Perfume });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(CosmeticLine.Soap, (await GetAsync(saved.Id)).Line);
    }

    [Fact]
    public async Task Deleted_recipe_is_soft_deleted_and_cannot_be_saved_again()
    {
        var saved = await _http.SaveAsync(Recipe(Unique("Тимчасове")));

        await _http.DeleteAsync($"/api/recipes/{saved.Id}");

        var row = await factory.WithDbAsync(db => db.Recipes.IgnoreQueryFilters().SingleAsync(r => r.Id == saved.Id));
        Assert.True(row.IsDeleted);
        var res = await _http.PutAsJsonAsync($"/api/recipes/{saved.Id}", saved with { Version = row.Version });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Id_in_the_path_must_match_the_body()
    {
        var res = await _http.PutAsJsonAsync($"/api/recipes/{Guid.NewGuid()}", Recipe("Рецепт"));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
