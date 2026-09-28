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

        switch (builder.Configuration.GetValue("Database:Provider", DatabaseProvider.Sqlite))
        {
            case DatabaseProvider.Sqlite:
                connectionString = SqlitePaths.Resolve(connectionString, builder.Environment.ContentRootPath);
                builder.Services.AddDbContext<SoapAndSoulDbContext>(o => o.UseSqlite(connectionString));
                break;
            case DatabaseProvider.SqlServer:
                builder.Services.AddDbContext<SoapAndSoulDbContext>(o => o.UseSqlServer(connectionString, SqlServerDatabase.Configure));
                break;
            default:
                throw new InvalidOperationException("Database:Provider must be Sqlite or SqlServer.");
        }
    }
}
