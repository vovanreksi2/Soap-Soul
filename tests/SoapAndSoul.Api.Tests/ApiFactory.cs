using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SoapAndSoul.Api.Tests;

/// <summary>Runs the API against a throwaway SQLite file and image folder.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "soapandsoul-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={Path.Combine(_dir, "test.db")};Pooling=False");
        builder.UseSetting("Database:SeedSampleData", "false");
        builder.UseSetting("Images:Path", Path.Combine(_dir, "images"));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }
}
