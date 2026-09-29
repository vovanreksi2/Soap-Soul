using System.Net.Http.Json;
using Microsoft.Playwright;
using SoapAndSoul.Domain.Contracts;

namespace SoapAndSoul.UI.Tests;

/// <summary>One browser page per test; fails the test if the app showed its error bar.</summary>
[Collection(AppCollection.Name)]
public abstract class UiTest(AppFixture app) : IAsyncLifetime
{
    protected HttpClient Api => app.Api;

    protected IPage Page { get; private set; } = null!;

    public async Task InitializeAsync() => Page = await app.NewPageAsync();

    public async Task DisposeAsync()
    {
        try
        {
            await Assertions.Expect(Page.Locator("#blazor-error-ui")).ToBeHiddenAsync(new() { Timeout = 1000 });
        }
        finally
        {
            await Page.Context.CloseAsync();
        }
    }

    /// <summary>Opens a client route and waits for the Blazor app to render its screen.</summary>
    protected async Task OpenAsync(string url)
    {
        await Page.GotoAsync(url);
        await Assertions.Expect(Page.Locator(".screen")).ToBeVisibleAsync();
    }

    protected ILocator Dialog(string label) => Page.GetByRole(AriaRole.Dialog, new() { Name = label });

    protected ILocator Toast(string text) => Page.Locator(".toast", new() { HasText = text });

    /// <summary>Waits for the debounced auto-save to reach the server.</summary>
    protected async Task<RecipeDto> SavedRecipeAsync(Guid id, Func<RecipeDto, bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var recipe = await Api.GetFromJsonAsync<RecipeDto>($"/api/recipes/{id}");
            if (condition(recipe!)) return recipe!;
            if (DateTime.UtcNow > deadline) Assert.Fail($"The recipe was not saved as expected. Last copy: {recipe}");
            await Task.Delay(200);
        }
    }
}
