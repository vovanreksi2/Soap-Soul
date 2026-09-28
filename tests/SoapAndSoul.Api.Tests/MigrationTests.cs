using Microsoft.EntityFrameworkCore;
using SoapAndSoul.Data;
using SoapAndSoul.Data.SqlServer;

namespace SoapAndSoul.Api.Tests;

/// <summary>Each provider has its own migration set; both must be regenerated when the model changes.</summary>
public class MigrationTests
{
    [Fact]
    public void Sqlite_migrations_match_the_model() =>
        AssertNoPendingChanges(new DbContextOptionsBuilder<SoapAndSoulDbContext>().UseSqlite("Data Source=:memory:"));

    [Fact]
    public void SqlServer_migrations_match_the_model() =>
        AssertNoPendingChanges(new DbContextOptionsBuilder<SoapAndSoulDbContext>()
            .UseSqlServer("Server=.;Database=SoapAndSoul", SqlServerDatabase.Configure));

    private static void AssertNoPendingChanges(DbContextOptionsBuilder<SoapAndSoulDbContext> options)
    {
        using var db = new SoapAndSoulDbContext(options.Options);
        Assert.False(db.Database.HasPendingModelChanges(), "Run 'dotnet ef migrations add' for this provider.");
    }
}
