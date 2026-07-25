using GNAS.Core;

namespace GNAS.Tests.Integration.Core;

public sealed class SqliteLeaseServiceTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExpiredLease_CanBeTakenOverWithHigherFencingToken()
    {
        var root = Path.Combine("TestArtifacts", nameof(SqliteLeaseServiceTests), Guid.CreateVersion7().ToString("N"));
        var leases = new SqliteLeaseService(new DatabaseProvider(root));
        var first = await leases.AcquireAsync("backup:daily", "node-a", TimeSpan.FromMilliseconds(20), CancellationToken.None);
        Assert.NotNull(first);

        Assert.Null(await leases.AcquireAsync("backup:daily", "node-b", TimeSpan.FromMinutes(1), CancellationToken.None));
        await Task.Delay(40);

        var takeover = await leases.AcquireAsync("backup:daily", "node-b", TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.NotNull(takeover);
        Assert.True(takeover.FencingToken > first.FencingToken);
        Assert.False(await leases.RenewAsync(first, TimeSpan.FromMinutes(1), CancellationToken.None));
    }
}
