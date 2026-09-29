using Microsoft.Data.Sqlite;

namespace SoapAndSoul.Api.Tests;

/// <summary>
/// A named SQLite database that lives only in memory. Every DbContext opens its own connection to it
/// (shared cache), so concurrent requests work; one connection is held open to keep the data alive.
/// Unlike the EF in-memory provider it runs the real migrations, query filters and concurrency checks.
/// </summary>
public sealed class InMemoryDatabase : IDisposable
{
    private readonly SqliteConnection _keepAlive;

    public InMemoryDatabase()
    {
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"soapandsoul-{Guid.NewGuid():N}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
        _keepAlive = new SqliteConnection(ConnectionString);
        _keepAlive.Open();
    }

    public string ConnectionString { get; }

    public void Dispose() => _keepAlive.Dispose();
}
