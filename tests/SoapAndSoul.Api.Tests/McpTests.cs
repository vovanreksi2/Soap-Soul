using System.Net;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace SoapAndSoul.Api.Tests;

/// <summary>Drives the MCP endpoint with a real MCP client, as an assistant would.</summary>
public class McpTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<McpClient> ConnectAsync(HttpClient? http = null)
    {
        http ??= factory.CreateClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp"), TransportMode = HttpTransportMode.StreamableHttp },
            http, ownsHttpClient: false);
        return await McpClient.CreateAsync(transport);
    }

    private static async Task<JsonElement> Call(McpClient mcp, string tool, Dictionary<string, object?> args)
    {
        var result = await mcp.CallToolAsync(tool, args);
        var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(t => t.Text));
        Assert.True(result.IsError != true, text);
        return JsonDocument.Parse(text).RootElement;
    }

    [Fact]
    public async Task Lists_the_main_tools()
    {
        await using var mcp = await ConnectAsync();

        var names = (await mcp.ListToolsAsync()).Select(t => t.Name).ToList();

        Assert.Superset(new HashSet<string>
        {
            "list_categories", "list_ingredients", "create_ingredient", "update_ingredient", "delete_ingredient",
            "list_recipes", "get_recipe", "create_recipe", "update_recipe",
            "add_recipe_ingredient", "set_recipe_ingredient_amount", "remove_recipe_ingredient", "delete_recipe",
        }, names.ToHashSet());
    }

    [Fact]
    public async Task Builds_a_recipe_with_the_app_rules()
    {
        await using var mcp = await ConnectAsync();

        var mold = await Call(mcp, "create_ingredient", new()
        {
            ["line"] = "Soap", ["category"] = "Mold", ["name"] = "Форма MCP", ["purchasePrice"] = 200, ["purchaseQuantity"] = 1, ["capacity"] = 110,
        });
        Assert.Equal(100, mold.GetProperty("usesPerItem").GetInt32());
        var soapBase = await Call(mcp, "create_ingredient", new()
        {
            ["line"] = "Soap", ["category"] = "SoapBase", ["name"] = "Основа MCP", ["purchasePrice"] = 400, ["purchaseQuantity"] = 1000, ["typicalAmount"] = 100,
        });
        var recipe = await Call(mcp, "create_recipe", new() { ["line"] = "Soap", ["name"] = "Рецепт MCP" });
        var id = recipe.GetProperty("id").GetString();

        await Call(mcp, "add_recipe_ingredient", new() { ["recipeId"] = id, ["ingredientId"] = soapBase.GetProperty("id").GetString() });
        var view = await Call(mcp, "add_recipe_ingredient", new() { ["recipeId"] = id, ["ingredientId"] = mold.GetProperty("id").GetString() });

        // The mold's capacity became the weight and the base amount.
        Assert.Equal(110, view.GetProperty("weight").GetDecimal());
        var baseItem = view.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("category").GetString() == "SoapBase");
        Assert.Equal(110, baseItem.GetProperty("amount").GetDecimal());
        Assert.Equal(46, view.GetProperty("costPerPiece").GetDecimal()); // 110 г × 0,40 + 200 / 100
        Assert.Contains("Колір", view.GetProperty("missingRequired").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Invalid_edit_is_a_tool_error_the_assistant_can_read()
    {
        await using var mcp = await ConnectAsync();

        var result = await mcp.CallToolAsync("create_recipe", new Dictionary<string, object?> { ["line"] = "Soap", ["name"] = " " });

        Assert.True(result.IsError);
        Assert.Contains("Вкажіть назву.", string.Concat(result.Content.OfType<TextContentBlock>().Select(t => t.Text)));
    }

    [Fact]
    public async Task Without_a_key_mcp_stays_off_unless_anonymous_is_allowed()
    {
        using var locked = factory.WithWebHostBuilder(b => b.UseSetting("Mcp:AllowAnonymous", "false"));

        var res = await locked.CreateClient().PostAsync("/mcp", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Api_key_is_required_when_configured()
    {
        using var secured = factory.WithWebHostBuilder(b => b.UseSetting("Mcp:ApiKey", "secret"));

        var anonymous = await secured.CreateClient().PostAsync("/mcp", new StringContent("{}"));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var http = secured.CreateClient();
        http.DefaultRequestHeaders.Authorization = new("Bearer", "secret");
        await using var mcp = await ConnectAsync(http);
        Assert.NotEmpty(await mcp.ListToolsAsync());
    }
}
