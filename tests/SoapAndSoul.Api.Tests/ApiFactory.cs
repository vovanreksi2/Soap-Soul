using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SoapAndSoul.Data;

namespace SoapAndSoul.Api.Tests;

/// <summary>Runs the API against its own in-memory SQLite database and a throwaway image folder.</summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly InMemoryDatabase _database = new();
    private readonly string _images = Path.Combine(Path.GetTempPath(), "soapandsoul-tests", Guid.NewGuid().ToString("N"));

    /// <summary>Fill the database with the development sample data (and the supplier catalog it uses) at startup.</summary>
    public bool SeedSampleData { get; init; }

    /// <summary>Seed the supplier catalog at startup.</summary>
    public bool SeedCatalog { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // Serves the Blazor client from its build output, as Development does.
        builder.UseStaticWebAssets();
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("ConnectionStrings:Default", _database.ConnectionString);
        builder.UseSetting("Database:SeedCatalog", SeedCatalog ? "true" : "false");
        builder.UseSetting("Database:SeedSampleData", SeedSampleData ? "true" : "false");
        builder.UseSetting("Images:Provider", "Local");
        builder.UseSetting("Images:Path", _images);
        builder.UseSetting("Llm:Provider", "None");
        builder.UseSetting("Mcp:AllowAnonymous", "true");
    }

    /// <summary>Runs <paramref name="action"/> with a fresh DbContext, to check or arrange stored rows directly.</summary>
    public async Task<T> WithDbAsync<T>(Func<SoapAndSoulDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<SoapAndSoulDbContext>());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        _database.Dispose();
        try { Directory.Delete(_images, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
