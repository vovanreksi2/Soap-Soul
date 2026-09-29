using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ModelContextProtocol;

namespace SoapAndSoul.Api.Mcp;

public sealed class McpOptions
{
    public const string Path = "/mcp";

    public bool Enabled { get; set; } = true;

    /// <summary>When set, clients must send <c>Authorization: Bearer &lt;key&gt;</c>.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Serve MCP without a key (development). Otherwise a missing key keeps the endpoint off.</summary>
    public bool AllowAnonymous { get; set; }

    public bool IsServed => Enabled && (AllowAnonymous || !string.IsNullOrEmpty(ApiKey));
}

/// <summary>
/// MCP server (Streamable HTTP, stateless) at <c>/mcp</c> with tools for the catalog and recipes, so an
/// assistant such as Claude can read and edit the same data as the app.
/// </summary>
public static class McpSetup
{
    public static void AddSoapAndSoulMcp(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection("Mcp").Get<McpOptions>() ?? new McpOptions();
        if (!options.IsServed) return;
        // Keep Ukrainian text readable in tool results instead of \uXXXX escapes.
        var json = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        builder.Services.AddMcpServer(o => o.ServerInfo = new() { Name = "soap-and-soul", Version = "1.0" })
            .WithHttpTransport(o => o.Stateless = true)
            .WithTools<IngredientTools>(json)
            .WithTools<RecipeTools>(json);
    }

    public static void MapSoapAndSoulMcp(this WebApplication app)
    {
        var options = app.Configuration.GetSection("Mcp").Get<McpOptions>() ?? new McpOptions();
        if (!options.IsServed)
        {
            if (options.Enabled) app.Logger.LogWarning("MCP is off: set Mcp:ApiKey (or Mcp:AllowAnonymous for development).");
            app.Map(McpOptions.Path, () => Results.NotFound()); // not the app shell
            return;
        }
        if (!string.IsNullOrEmpty(options.ApiKey))
        {
            // Middleware rather than an endpoint filter, so the key is checked before anything else about the request.
            var expected = Encoding.UTF8.GetBytes("Bearer " + options.ApiKey);
            app.Use(async (context, next) =>
            {
                if (context.Request.Path.StartsWithSegments(McpOptions.Path)
                    && !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(context.Request.Headers.Authorization.ToString()), expected))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }
                await next(context);
            });
        }
        app.MapMcp(McpOptions.Path);
    }
}
