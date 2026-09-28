using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SoapAndSoul.Data;

/// <summary>Used only by <c>dotnet ef</c> to create migrations.</summary>
internal sealed class DesignTimeFactory : IDesignTimeDbContextFactory<SoapAndSoulDbContext>
{
    public SoapAndSoulDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SoapAndSoulDbContext>().UseSqlite("Data Source=design.db").Options);
}
