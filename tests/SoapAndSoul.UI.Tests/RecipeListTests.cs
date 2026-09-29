using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using SoapAndSoul.Domain.Catalog;
using SoapAndSoul.Domain.Contracts;
using static Microsoft.Playwright.Assertions;
using static SoapAndSoul.Api.Tests.TestCatalog;

namespace SoapAndSoul.UI.Tests;

public class RecipeListTests(AppFixture app) : UiTest(app)
{
    private ILocator Row(string name) => Page.Locator(".swipe", new() { HasText = name });

    [Fact]
    public async Task Start_page_opens_the_soap_list_and_switches_to_perfume()
    {
        var soap = await Api.SaveAsync(Recipe(Unique("Лавандове")));
        var perfume = await Api.SaveAsync(Recipe(CosmeticLine.Perfume, Unique("Жасмин")));

        await OpenAsync("/");

        await Expect(Page).ToHaveURLAsync(new Regex("/soap$"));
        await Expect(Row(soap.Name)).ToBeVisibleAsync();
        await Expect(Row(perfume.Name)).ToHaveCountAsync(0);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Парфуми" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/perfume$"));
        await Expect(Row(perfume.Name)).ToBeVisibleAsync();
        await Expect(Row(soap.Name)).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Row_shows_the_cost_of_one_piece()
    {
        var mold = await Api.SaveAsync(Mold(Unique("Форма"), capacity: 110, price: 200));
        var soapBase = await Api.SaveAsync(SoapBase(Unique("Основа"), pricePerKg: 400));
        var recipe = await Api.SaveAsync(Recipe(Unique("Кошторис"), new(mold.Id, 1), new(soapBase.Id, 110)));

        await OpenAsync("/soap");

        // 110 г × 0,40 грн + 200 грн / 100 використань
        await Expect(Row(recipe.Name).Locator(".row-cost b")).ToHaveTextAsync("46 грн");
        await Expect(Row(recipe.Name).Locator(".row-meta")).ToContainTextAsync("2 комп.");
    }

    [Fact]
    public async Task Search_narrows_the_list()
    {
        var suffix = Unique("");
        var coconut = await Api.SaveAsync(Recipe("Кокосове" + suffix));
        var chocolate = await Api.SaveAsync(Recipe("Шоколадне" + suffix));
        await OpenAsync("/soap");
        await Expect(Row(chocolate.Name)).ToBeVisibleAsync();

        await Page.GetByPlaceholder("Пошук за назвою чи описом").FillAsync("кокосове");

        await Expect(Row(coconut.Name)).ToBeVisibleAsync();
        await Expect(Row(chocolate.Name)).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task New_recipe_is_created_named_and_listed()
    {
        var name = Unique("Медове");
        await OpenAsync("/soap");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Новий рецепт" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/soap/recipes/[0-9a-f-]+\\?new=true$"));
        await Expect(Toast("Рецепт створено")).ToBeVisibleAsync();
        var title = Page.GetByLabel("Назва рецепта");
        await Expect(title).ToBeFocusedAsync();
        await title.FillAsync(name);
        await title.PressAsync("Enter");
        await Expect(Page.Locator(".title-btn")).ToHaveTextAsync(name);

        var id = Guid.Parse(new Uri(Page.Url).Segments[^1]);
        await SavedRecipeAsync(id, r => r.Name == name);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Рецепти" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/soap$"));
        await Expect(Row(name)).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Duplicate_adds_a_copy_to_the_list()
    {
        var recipe = await Api.SaveAsync(Recipe(Unique("Оригінал")));
        await OpenAsync("/soap");

        await Row(recipe.Name).GetByRole(AriaRole.Button, new() { Name = "Дублювати" }).ClickAsync();

        await Expect(Toast("Рецепт продубльовано")).ToBeVisibleAsync();
        await Expect(Row(recipe.Name + " (копія)")).ToBeVisibleAsync();
        var list = await Api.GetFromJsonAsync<List<RecipeDto>>("/api/recipes?line=Soap");
        Assert.Contains(list!, r => r.Name == recipe.Name + " (копія)");
    }

    [Fact]
    public async Task Delete_from_the_list_asks_first_and_removes_the_recipe()
    {
        var recipe = await Api.SaveAsync(Recipe(Unique("Зайве")));
        await OpenAsync("/soap");

        await Row(recipe.Name).GetByRole(AriaRole.Button, new() { Name = "Видалити" }).ClickAsync();
        var confirm = Dialog("Видалити рецепт?");
        await Expect(confirm).ToContainTextAsync(recipe.Name);
        await confirm.GetByRole(AriaRole.Button, new() { Name = "Видалити" }).ClickAsync();

        await Expect(Toast("Рецепт видалено")).ToBeVisibleAsync();
        await Expect(Row(recipe.Name)).ToHaveCountAsync(0);
        Assert.Equal(HttpStatusCode.NotFound, (await Api.GetAsync($"/api/recipes/{recipe.Id}")).StatusCode);
    }
}
