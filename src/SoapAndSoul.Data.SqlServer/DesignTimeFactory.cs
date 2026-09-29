using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SoapAndSoul.Data.SqlServer;

/// <summary>Used only by <c>dotnet ef</c> to create SQL Server migrations; never connects.</summary>
internal sealed class DesignTimeFactory : IDesignTimeDbContextFactory<SoapAndSoulDbContext>
{
    public SoapAndSoulDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SoapAndSoulDbContext>()
            .UseSqlServer("Server=.;Database=SoapAndSoul", SqlServerDatabase.Configure)
            .Options);
}
