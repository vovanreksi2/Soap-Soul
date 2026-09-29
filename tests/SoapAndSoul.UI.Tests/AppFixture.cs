using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using SoapAndSoul.Api.Tests;

namespace SoapAndSoul.UI.Tests;

/// <summary>
/// The whole app (API + Blazor client) on a real Kestrel port with an in-memory database, and one Chromium
/// shared by the tests. Run with <c>HEADED=1</c> to watch the browser.
/// </summary>
public sealed class AppFixture : IAsyncLifetime
{
    private readonly ApiFactory _app = new();
    private IPlaywright? _playwright;

    public IBrowser Browser { get; private set; } = null!;

    /// <summary>Talks to the same API as the browser, to arrange data and check what was saved.</summary>
    public HttpClient Api { get; private set; } = null!;

    public Uri BaseAddress { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _app.UseKestrel(0);
        _app.StartServer();
        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        BaseAddress = new Uri(address.Replace("[::]", "localhost").Replace("127.0.0.1", "localhost"));
        Api = new HttpClient { BaseAddress = BaseAddress };

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new()
        {
            Headless = Environment.GetEnvironmentVariable("HEADED") is not ("1" or "true"),
            SlowMo = Environment.GetEnvironmentVariable("HEADED") is "1" or "true" ? 150 : null,
        });
    }

    /// <summary>A fresh phone-sized browser session (no shared storage between tests).</summary>
    public async Task<IPage> NewPageAsync()
    {
        var context = await Browser.NewContextAsync(new()
        {
            BaseURL = BaseAddress.ToString(),
            ViewportSize = new() { Width = 390, Height = 844 },
            IsMobile = true,
            HasTouch = true,
            Locale = "uk-UA",
            ServiceWorkers = ServiceWorkerPolicy.Block,
        });
        context.SetDefaultTimeout(15_000);
        return await context.NewPageAsync();
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null) await Browser.DisposeAsync();
        _playwright?.Dispose();
        Api?.Dispose();
        await _app.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class AppCollection : ICollectionFixture<AppFixture>
{
    public const string Name = "App";
}
