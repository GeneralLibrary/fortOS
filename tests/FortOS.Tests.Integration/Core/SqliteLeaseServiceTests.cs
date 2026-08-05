using FortOS.Core;

namespace FortOS.Tests.Integration.Core;

public sealed class SqliteLeaseServiceTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExpiredLease_CanBeTakenOverWithHigherFencingToken()
    {
        var root = Path.Combine("TestArtifacts", nameof(SqliteLeaseServiceTests), Guid.CreateVersion7().ToString("N"));
        var leases = new SqliteLeaseService(new DatabaseProvider(root));
        // TTL 用 500ms：太短（如 20ms）在慢速 CI/容器环境下第二次获取前就可能过期，
        // 导致「未过期应拒绝」的断言时序脆弱。
        var first = await leases.AcquireAsync("backup:daily", "node-a", TimeSpan.FromMilliseconds(500), CancellationToken.None);
        Assert.NotNull(first);

        // 租约未过期时，其他 owner 必须被拒绝。
        Assert.Null(await leases.AcquireAsync("backup:daily", "node-b", TimeSpan.FromMinutes(1), CancellationToken.None));
        await Task.Delay(600); // 等待过期

        var takeover = await leases.AcquireAsync("backup:daily", "node-b", TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.NotNull(takeover);
        Assert.True(takeover.FencingToken > first.FencingToken);
        Assert.False(await leases.RenewAsync(first, TimeSpan.FromMinutes(1), CancellationToken.None));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ConcurrentAcquire_DifferentOwners_ExactlyOneWins()
    {
        // 回归：旧实现用 deferred 事务，两个并发获取者会同时读到「无记录」并各自写入
        // 重复的 fencing token（同一任务并发执行或 SQLITE_BUSY 500）。BEGIN IMMEDIATE
        // 串行化后，同一租约同一时刻只能有一个持有者成功。
        var root = Path.Combine("TestArtifacts", nameof(SqliteLeaseServiceTests), Guid.CreateVersion7().ToString("N"));
        var leases = new SqliteLeaseService(new DatabaseProvider(root));

        var results = await Task.WhenAll(
            leases.AcquireAsync("backup:daily", "node-a", TimeSpan.FromMinutes(1), CancellationToken.None),
            leases.AcquireAsync("backup:daily", "node-b", TimeSpan.FromMinutes(1), CancellationToken.None),
            leases.AcquireAsync("backup:daily", "node-c", TimeSpan.FromMinutes(1), CancellationToken.None));

        Assert.Single(results, result => result is not null);
    }
}
