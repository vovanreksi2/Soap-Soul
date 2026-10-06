using Microsoft.EntityFrameworkCore;
using SoapAndSoul.Data;
using SoapAndSoul.Data.SqlServer;

namespace SoapAndSoul.Api;

public enum DatabaseProvider { Sqlite, SqlServer }

internal static class Database
{
    /// <summary>
    /// SQLite for development and tests, SQL Server (Azure SQL) in production. Each provider has its own
    /// migrations: SQLite in <c>SoapAndSoul.Data</c>, SQL Server in <c>SoapAndSoul.Data.SqlServer</c>.
    /// </summary>
    public static void AddDatabase(this WebApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
        var configure = Options(builder.Configuration, builder.Environment.ContentRootPath, connectionString);
        builder.Services.AddDbContext<SoapAndSoulDbContext>(o => configure(o));
    }

    /// <summary>
    /// Copies the database named by <c>Database:ImportFrom</c> (a connection string for the same provider) into
    /// the app's database if that is still empty. Runs after migrations and before any seeding.
    /// </summary>
    public static async Task ImportIfConfiguredAsync(this WebApplication app, SoapAndSoulDbContext target)
    {
        if (app.Configuration["Database:ImportFrom"] is not { Length: > 0 } sourceConnectionString) return;

        var options = new DbContextOptionsBuilder<SoapAndSoulDbContext>();
        Options(app.Configuration, app.Environment.ContentRootPath, sourceConnectionString)(options);
        await using var source = new SoapAndSoulDbContext(options.Options);

        var result = await DatabaseImport.CopyIfEmptyAsync(source, target);
        if (result is null)
            app.Logger.LogInformation("Database import skipped: the database already has data.");
        else
            app.Logger.LogInformation("Database import: copied {Ingredients} ingredients and {Recipes} recipes.",
                result.Ingredients, result.Recipes);
    }

    private static Action<DbContextOptionsBuilder> Options(IConfiguration configuration, string contentRoot, string connectionString)
    {
        switch (configuration.GetValue("Database:Provider", DatabaseProvider.Sqlite))
        {
            case DatabaseProvider.Sqlite:
                connectionString = SqlitePaths.Resolve(connectionString, contentRoot);
                return o => o.UseSqlite(connectionString);
            case DatabaseProvider.SqlServer:
                return o => o.UseSqlServer(connectionString, SqlServerDatabase.Configure);
            default:
                throw new InvalidOperationException("Database:Provider must be Sqlite or SqlServer.");
        }
    }
}
