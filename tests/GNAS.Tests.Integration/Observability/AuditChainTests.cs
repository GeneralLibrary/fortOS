using GNAS.Core;
using global::GNAS.Observability.Audit;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace GNAS.Tests.Integration.Observability;

public sealed class AuditChainTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task AppendThreeEntries_VerifyIntegrity_ReturnsValid()
    {
        var database = new DatabaseProvider(ObservabilityTestPaths.CreateDataRoot(nameof(AppendThreeEntries_VerifyIntegrity_ReturnsValid)));
        var chain = new AuditChain(database, new TestKeyStore());

        for (var i = 0; i < 3; i++)
        {
            await chain.AppendAsync(CreateAuditEntry(i), CancellationToken.None);
        }

        var result = await chain.VerifyIntegrityAsync(null, null, CancellationToken.None);
        Assert.True(result.IsValid, result.Message);
        Assert.Equal(3, result.TotalEntries);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task VerifyIntegrityAsync_WhenTampered_DetectsBrokenSequence()
    {
        var database = new DatabaseProvider(ObservabilityTestPaths.CreateDataRoot(nameof(VerifyIntegrityAsync_WhenTampered_DetectsBrokenSequence)));
        var chain = new AuditChain(database, new TestKeyStore());
        for (var i = 0; i < 3; i++) await chain.AppendAsync(CreateAuditEntry(i), CancellationToken.None);

        await using (var connection = await database.GetConnectionAsync(CancellationToken.None))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE audit_chain SET current_hash = 'tampered' WHERE sequence = 2;";
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }

        var result = await chain.VerifyIntegrityAsync(null, null, CancellationToken.None);
        Assert.False(result.IsValid);
        Assert.Equal(2, result.BrokenAtSequence);
    }

    private static LogEntry CreateAuditEntry(int index) => new()
    {
        Category = LogCategory.Audit,
        Level = LogLevel.Information,
        SourceComponent = "test",
        UserId = "admin",
        Message = "audit " + index,
        Audit = new AuditDetail
        {
            Action = "file.read",
            Resource = "/data/" + index,
            ResourceType = "file",
            Granted = true,
            AfterState = "{}",
            CurrentHash = string.Empty,
            ChainSignature = string.Empty
        }
    };
}
