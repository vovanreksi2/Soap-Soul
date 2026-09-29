using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using SoapAndSoul.Domain.Catalog;
using SoapAndSoul.Domain.Contracts;
using static Microsoft.Playwright.Assertions;
using static SoapAndSoul.Api.Tests.TestCatalog;

namespace SoapAndSoul.UI.Tests;

public class RecipeScreenTests(AppFixture app) : UiTest(app)
{
    private ILocator EmptySlot(string label) => Page.Locator(".slot-empty", new() { HasText = label });

    private ILocator Item(string name) => Page.Locator(".slots .item", new() { HasText = name });

    private async Task<RecipeDto> OpenNewRecipeAsync()
    {
        var recipe = await Api.SaveAsync(Recipe(Unique("Рецепт")) with { Weight = 0 });
        await OpenAsync($"/soap/recipes/{recipe.Id}");
        await Expect(Page.Locator(".title-btn")).ToHaveTextAsync(recipe.Name);
        return recipe;
    }

    [Fact]
    public async Task Picking_a_mold_and_a_base_sets_weight_cost_and_saves()
    {
        var mold = await Api.SaveAsync(Mold(Unique("Форма"), capacity: 110, price: 200));
        var soapBase = await Api.SaveAsync(SoapBase(Unique("Основа"), pricePerKg: 400));
        var recipe = await OpenNewRecipeAsync();
        await Expect(Page.Locator(".req")).ToContainTextAsync("Обов’язкові 0/5");

        await EmptySlot("Форма").ClickAsync();
        await Dialog("Форма").Locator(".pick", new() { HasText = mold.Name }).ClickAsync();
        await Expect(Dialog("Форма")).ToHaveCountAsync(0);
        await EmptySlot("Основа").ClickAsync();
        await Dialog("Основа").Locator(".pick", new() { HasText = soapBase.Name }).ClickAsync();
        await Expect(Dialog("Основа")).ToHaveCountAsync(0);

        // The mold's capacity became the weight and the base amount.
        await Expect(Page.Locator(".metric").First).ToHaveTextAsync("110 г");
        await Expect(Item(soapBase.Name).GetByLabel($"Кількість: {soapBase.Name}")).ToHaveValueAsync("110");
        await Expect(Page.Locator(".costbar .cost b")).ToHaveTextAsync("46 грн");
        await Expect(Page.Locator(".req")).ToContainTextAsync("Обов’язкові 2/5");

        await Page.GetByLabel("Партія, шт").Locator("..").GetByRole(AriaRole.Button, new() { Name = "Більше" }).ClickAsync();
        await Expect(Page.Locator(".batch-total")).ToHaveTextAsync("Партія: 92 грн · 220 г");

        var saved = await SavedRecipeAsync(recipe.Id, r => r.BatchSize == 2);
        Assert.Equal([new(mold.Id, 1), new(soapBase.Id, 110)], saved.Items);
        Assert.Equal(110, saved.Weight);
        await Expect(Page.Locator(".save-state")).ToHaveTextAsync("Збережено");
    }

    [Fact]
    public async Task Stepper_changes_an_amount()
    {
        var pigment = await Api.SaveAsync(Pigment(Unique("Бірюза"), typicalDrops: 3));
        var recipe = await OpenNewRecipeAsync();

        await EmptySlot("Колір").ClickAsync();
        var picker = Dialog("Колір");
        await picker.Locator(".pick", new() { HasText = pigment.Name }).ClickAsync();
        await Expect(picker.Locator(".pick.on", new() { HasText = pigment.Name })).ToBeVisibleAsync();
        await picker.GetByRole(AriaRole.Button, new() { Name = "Готово" }).ClickAsync();

        var amount = Item(pigment.Name).GetByLabel($"Кількість: {pigment.Name}");
        await Expect(amount).ToHaveValueAsync("3");
        await Item(pigment.Name).GetByRole(AriaRole.Button, new() { Name = "Більше" }).ClickAsync();
        await Expect(amount).ToHaveValueAsync("4");

        var saved = await SavedRecipeAsync(recipe.Id, r => r.Items.Count == 1 && r.Items[0].Amount == 4);
        Assert.Equal(pigment.Id, saved.Items[0].IngredientId);
    }

    [Fact]
    public async Task New_ingredient_from_the_picker_goes_into_the_catalog_and_the_recipe()
    {
        var name = Unique("Перламутр");
        var recipe = await OpenNewRecipeAsync();

        await EmptySlot("Колір").ClickAsync();
        await Dialog("Колір").GetByRole(AriaRole.Button, new() { Name = "Новий: колір" }).ClickAsync();
        var form = Dialog("Новий: колір");
        await form.GetByLabel("Назва").FillAsync(name);
        await form.GetByLabel("Типова порція на рецепт").FillAsync("5");
        await form.GetByLabel("Кількість", new() { Exact = true }).FillAsync("10");
        await form.GetByLabel("Ціна").FillAsync("90");
        await Expect(form.Locator(".hint-box")).ToContainTextAsync("Ціна за");
        await form.GetByRole(AriaRole.Button, new() { Name = "Додати" }).ClickAsync();

        await Expect(form).ToHaveCountAsync(0);
        await Expect(Toast("Додано в рецепт")).ToBeVisibleAsync();
        await Dialog("Колір").GetByRole(AriaRole.Button, new() { Name = "Готово" }).ClickAsync();
        await Expect(Item(name)).ToBeVisibleAsync();

        var catalog = await Api.GetFromJsonAsync<List<IngredientDto>>("/api/ingredients?line=Soap");
        var created = Assert.Single(catalog!, i => i.Name == name);
        Assert.Equal(CategoryKey.Pigment, created.Category);
        Assert.Equal(90, created.PurchasePrice);
        await SavedRecipeAsync(recipe.Id, r => r.Items.SequenceEqual([new RecipeItemDto(created.Id, 5)]));
    }

    [Fact]
    public async Task Title_description_and_time_are_edited_in_place()
    {
        var recipe = await OpenNewRecipeAsync();
        var name = Unique("Вівсяне");

        await Page.Locator(".title-btn").ClickAsync();
        await Page.GetByLabel("Назва рецепта").FillAsync(name);
        await Page.GetByLabel("Назва рецепта").PressAsync("Enter");
        await Page.GetByLabel("Опис").FillAsync("З вівсяним борошном");
        await Page.Locator(".metric", new() { HasText = "хв" }).ClickAsync();
        await Page.GetByLabel("Час, хв").FillAsync("50");
        await Page.GetByLabel("Час, хв").PressAsync("Enter");

        await Expect(Page.Locator(".metric", new() { HasText = "хв" })).ToHaveTextAsync("50 хв");
        await SavedRecipeAsync(recipe.Id, r => r.Name == name && r.Description == "З вівсяним борошном" && r.TimeMinutes == 50);
        await Expect(Page).ToHaveTitleAsync($"{name} · Soap & Soul");
    }

    [Fact]
    public async Task Deleting_the_recipe_returns_to_the_list()
    {
        var recipe = await OpenNewRecipeAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Видалити рецепт" }).ClickAsync();
        await Dialog("Видалити рецепт?").GetByRole(AriaRole.Button, new() { Name = "Видалити" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/soap$"));
        await Expect(Toast("Рецепт видалено")).ToBeVisibleAsync();
        await Expect(Page.Locator(".swipe", new() { HasText = recipe.Name })).ToHaveCountAsync(0);
        Assert.Equal(HttpStatusCode.NotFound, (await Api.GetAsync($"/api/recipes/{recipe.Id}")).StatusCode);
    }

    [Fact]
    public async Task Unknown_recipe_shows_a_way_back()
    {
        await OpenAsync($"/soap/recipes/{Guid.CreateVersion7()}");

        await Expect(Page.GetByText("Рецепт не знайдено.")).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "До списку" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/soap$"));
    }
}
