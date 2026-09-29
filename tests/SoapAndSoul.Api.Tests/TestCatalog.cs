using System.Net.Http.Json;
using SoapAndSoul.Domain.Catalog;
using SoapAndSoul.Domain.Contracts;

namespace SoapAndSoul.Api.Tests;

/// <summary>Valid ingredients and recipes for arranging tests, and HTTP helpers that save them.</summary>
public static class TestCatalog
{
    /// <summary>A name no other test uses, so tests sharing a database do not see each other's rows.</summary>
    public static string Unique(string name) => $"{name} {Guid.NewGuid().ToString("N")[..6]}";

    public static IngredientDto Mold(string name = "Форма", decimal capacity = 100, decimal price = 180, int uses = Categories.DefaultMoldUses) =>
        new(Guid.CreateVersion7(), CosmeticLine.Soap, CategoryKey.Mold, name, MeasureUnit.Piece, 1, 1, price, capacity, uses, null);

    /// <summary>Soap base bought by the kilogram.</summary>
    public static IngredientDto SoapBase(string name = "Основа", decimal pricePerKg = 400) =>
        new(Guid.CreateVersion7(), CosmeticLine.Soap, CategoryKey.SoapBase, name, MeasureUnit.Gram, 100, 1000, pricePerKg, null, 1, null);

    public static IngredientDto Pigment(string name = "Колір", decimal typicalDrops = 3) =>
        new(Guid.CreateVersion7(), CosmeticLine.Soap, CategoryKey.Pigment, name, MeasureUnit.Drop, typicalDrops, 10, 100, null, 1, null);

    public static IngredientDto Fragrance(CosmeticLine line = CosmeticLine.Soap, string name = "Запашка") =>
        new(Guid.CreateVersion7(), line, CategoryKey.Fragrance, name, MeasureUnit.Drop, 10, 10, 150, null, 1, null);

    public static IngredientDto EssentialOil(string name = "Ефірне масло") =>
        new(Guid.CreateVersion7(), CosmeticLine.Soap, CategoryKey.EssentialOil, name, MeasureUnit.Drop, 10, 10, 200, null, 1, null);

    public static IngredientDto Bottle(string name = "Флакон", decimal volume = 30) =>
        new(Guid.CreateVersion7(), CosmeticLine.Perfume, CategoryKey.Bottle, name, MeasureUnit.Piece, 1, 1, 45, volume, 1, null);

    public static RecipeDto Recipe(CosmeticLine line, string name, params RecipeItemDto[] items) =>
        new(Guid.CreateVersion7(), line, name, "", 100, 30, 1, null, items);

    public static RecipeDto Recipe(string name, params RecipeItemDto[] items) => Recipe(CosmeticLine.Soap, name, items);

    public static async Task<IngredientDto> SaveAsync(this HttpClient http, IngredientDto ingredient) =>
        await PutAsync(http, $"/api/ingredients/{ingredient.Id}", ingredient);

    public static async Task<RecipeDto> SaveAsync(this HttpClient http, RecipeDto recipe) =>
        await PutAsync(http, $"/api/recipes/{recipe.Id}", recipe);

    private static async Task<T> PutAsync<T>(HttpClient http, string url, T body)
    {
        var res = await http.PutAsJsonAsync(url, body);
        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException($"PUT {url} → {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<T>())!;
    }
}
