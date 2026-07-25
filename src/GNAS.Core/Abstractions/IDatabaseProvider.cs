using Microsoft.Data.Sqlite;

namespace GNAS.Core;

/// <summary>SQLite 数据库提供器接口。</summary>
public interface IDatabaseProvider
{
    /// <summary>连接字符串。</summary>
    string ConnectionString { get; }
    /// <summary>获取已打开连接。</summary>
    Task<SqliteConnection> GetConnectionAsync(CancellationToken ct);
    /// <summary>初始化数据库结构。</summary>
    Task InitializeAsync(CancellationToken ct);
}
