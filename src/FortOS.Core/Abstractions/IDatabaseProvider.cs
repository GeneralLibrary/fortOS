using Microsoft.Data.Sqlite;

namespace FortOS.Core;

/// <summary>SQLite database provider interface.</summary>
public interface IDatabaseProvider
{
    /// <summary>Connection string.</summary>
    string ConnectionString { get; }
    /// <summary>Get an opened connection.</summary>
    Task<SqliteConnection> GetConnectionAsync(CancellationToken ct);
    /// <summary>Initialize the database schema.</summary>
    Task InitializeAsync(CancellationToken ct);
}
