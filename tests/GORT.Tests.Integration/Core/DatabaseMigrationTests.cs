using Microsoft.Data.Sqlite;
using GORT.Core;

namespace GORT.Tests.Integration.Core;

public sealed class DatabaseMigrationTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task Initialize_AppliesVersionedMigrationsIdempotently()
    {
        var root = Path.Combine("TestArtifacts", nameof(DatabaseMigrationTests), Guid.CreateVersion7().ToString("N"));
        var database = new DatabaseProvider(root);
        await database.InitializeAsync(CancellationToken.None);
        await database.InitializeAsync(CancellationToken.None);
        await using var connection = await database.GetConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM schema_migrations;";
        Assert.Equal(4L, (long)(await command.ExecuteScalarAsync(CancellationToken.None))!);
    }
}
