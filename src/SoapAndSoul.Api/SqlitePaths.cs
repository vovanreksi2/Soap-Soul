using Microsoft.Data.Sqlite;

namespace SoapAndSoul.Api;

internal static class SqlitePaths
{
    /// <summary>Makes a relative database file path absolute against the content root and creates its folder.</summary>
    public static string Resolve(string connectionString, string contentRoot)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (builder.DataSource is "" or ":memory:" || builder.Mode == SqliteOpenMode.Memory) return connectionString;
        builder.DataSource = Path.GetFullPath(builder.DataSource, contentRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(builder.DataSource)!);
        return builder.ToString();
    }
}
