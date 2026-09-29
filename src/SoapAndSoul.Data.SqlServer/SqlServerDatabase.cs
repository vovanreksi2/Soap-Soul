using Microsoft.EntityFrameworkCore.Infrastructure;

namespace SoapAndSoul.Data.SqlServer;

/// <summary>SQL Server (Azure SQL) setup. Its migrations live in this assembly, separate from the SQLite ones.</summary>
public static class SqlServerDatabase
{
    public static void Configure(SqlServerDbContextOptionsBuilder sql)
    {
        sql.MigrationsAssembly(typeof(SqlServerDatabase).Assembly.GetName().Name);
        // Azure SQL drops connections during failovers and scaling.
        sql.EnableRetryOnFailure();
    }
}
