using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SoapAndSoul.Domain.Contracts;

namespace SoapAndSoul.Api.Tests;

/// <summary>Startup: migrations, sample data, and what the app serves besides the API.</summary>
public class HostTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Startup_applies_every_migration()
    {
        factory.CreateClient();

        var pending = await factory.WithDbAsync(db => db.Database.GetPendingMigrationsAsync());

        Assert.Empty(pending);
    }

    [Fact]
    public async Task Health_check_answers_ok()
    {
        var res = await factory.CreateClient().GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Fresh_database_has_no_sample_data_unless_asked()
    {
        await using var seeded = new ApiFactory { SeedSampleData = true };
        var http = seeded.CreateClient();

        var recipes = await http.GetFromJsonAsync<List<RecipeDto>>("/api/recipes?line=Soap");
        var perfumes = await http.GetFromJsonAsync<List<RecipeDto>>("/api/recipes?line=Perfume");

        Assert.Contains(recipes!, r => r.Name == "Лавандове мило");
        Assert.Contains(perfumes!, r => r.Name == "Нічний жасмин");
        Assert.All(recipes!, r => Assert.NotEmpty(r.Items));
        // The database of this class's own factory is separate and stays empty.
        Assert.False(await factory.WithDbAsync(db => db.Recipes.AnyAsync(r => r.Name == "Лавандове мило")));
    }

    [Theory]
    [InlineData("/soap")]
    [InlineData("/perfume/recipes/0199a000-0000-7000-8000-000000000001")]
    public async Task Client_routes_get_the_app_shell(string url)
    {
        var res = await factory.CreateClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("_framework/blazor.webassembly.js", await res.Content.ReadAsStringAsync());
    }
}
