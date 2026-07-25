using GNAS.Core;
using GNAS.Observability.Audit;
using GNAS.Tests.Integration.Observability;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace GNAS.Tests.Integration.E2E;

public sealed class AuditChainE2ETests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task AppendVerifyTamper_VerificationDetectsBreak()
    {
        var database = new DatabaseProvider(CreateDataRoot(nameof(AppendVerifyTamper_VerificationDetectsBreak)));
        var chain = new AuditChain(database, new TestKeyStore());

        for (var i = 0; i < 4; i++)
        {
            await chain.AppendAsync(CreateAuditEntry(i), CancellationToken.None);
        }

        var valid = await chain.VerifyIntegrityAsync(null, null, CancellationToken.None);
        Assert.True(valid.IsValid, valid.Message);
        Assert.Equal(4, valid.TotalEntries);

        await using (var connection = await database.GetConnectionAsync(CancellationToken.None))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE audit_chain SET current_hash = 'tampered' WHERE sequence = 3;";
            Assert.Equal(1, await command.ExecuteNonQueryAsync(CancellationToken.None));
        }

        var tampered = await chain.VerifyIntegrityAsync(null, null, CancellationToken.None);
        Assert.False(tampered.IsValid);
        Assert.Equal(3, tampered.BrokenAtSequence);
    }

    private static string CreateDataRoot(string name)
    {
        var path = Path.GetFullPath(Path.Combine("TestArtifacts", "E2E", name, Guid.CreateVersion7().ToString()));
        Directory.CreateDirectory(path);
        return path;
    }

    private static LogEntry CreateAuditEntry(int index) => new()
    {
        Category = LogCategory.Audit,
        Level = LogLevel.Information,
        SourceComponent = "e2e",
        UserId = "user:admin",
        Message = "e2e audit " + index,
        Audit = new AuditDetail
        {
            Action = "e2e.step",
            Resource = "/e2e/" + index,
            ResourceType = "test",
            Granted = true,
            AfterState = "{\"index\":" + index + "}",
            CurrentHash = string.Empty,
            ChainSignature = string.Empty
        }
    };
}
