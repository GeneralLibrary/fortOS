using FortOS.Platform.Linux.Monitoring;

namespace FortOS.Tests.Integration.Platform;

public sealed class LinuxMonitoringParserTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void CpuCounters_CalculateExpectedUtilization()
    {
        var previous = LinuxProcParsers.ParseCpuCounters("cpu  100 0 50 850 0 0 0 0\n");
        var current = LinuxProcParsers.ParseCpuCounters("cpu  160 0 80 900 10 0 0 0\n");

        var metrics = LinuxProcParsers.CalculateCpuMetrics(previous, current);

        Assert.Equal(60, metrics.UsagePercent, 3);
        Assert.Equal(40, metrics.UserPercent, 3);
        Assert.Equal(20, metrics.SystemPercent, 3);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void MemoryAndOom_ParsesKernelValues()
    {
        var memory = LinuxProcParsers.ParseMemory("""
            MemTotal:       1000000 kB
            MemAvailable:    250000 kB
            SwapTotal:       100000 kB
            SwapFree:         40000 kB
            """);

        Assert.Equal(75, memory.UsedPercent, 3);
        Assert.Equal(60, memory.SwapUsedPercent, 3);
        Assert.Equal(3, LinuxProcParsers.ParseOomKillCount("pgfault 100\noom_kill 3\n"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TcpCounters_CalculateRetransmitRate()
    {
        const string header = "Tcp: RtoAlgorithm RtoMin RtoMax MaxConn ActiveOpens PassiveOpens AttemptFails EstabResets CurrEstab InSegs OutSegs RetransSegs";
        var previous = LinuxProcParsers.ParseTcpCounters(header + "\nTcp: 1 200 120000 -1 1 1 0 0 12 100 100 20\n");
        var current = LinuxProcParsers.ParseTcpCounters(header + "\nTcp: 1 200 120000 -1 1 1 0 0 14 100 100 30\n");

        var metrics = LinuxProcParsers.CalculateNetworkStack(previous, current, 5);

        Assert.Equal(14, metrics.EstablishedConnections);
        Assert.Equal(2, metrics.RetransmittedSegmentsPerSecond);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RaidParser_ReportsDegradedRecovery()
    {
        var arrays = LinuxProcParsers.ParseRaid("""
            Personalities : [raid1]
            md0 : active raid1 sdb1[1] sda1[0]
                  976630336 blocks super 1.2 [2/1] [U_]
                  [=>...................]  recovery =  8.5% (83000000/976630336) finish=100min speed=100K/sec
            """);

        var raid = Assert.Single(arrays);
        Assert.False(raid.Healthy);
        Assert.Equal(1, raid.ActiveDevices);
        Assert.Equal(2, raid.TotalDevices);
        Assert.Equal("recovery", raid.Operation);
        Assert.Equal(8.5, raid.ProgressPercent);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void DockerStatsParser_ConvertsBinaryAndDecimalSizes()
    {
        var containers = DockerStatsParser.Parse("""
            {"ID":"abc","Name":"web","CPUPerc":"12.5%","MemUsage":"128MiB / 1GiB","MemPerc":"12.5%","NetIO":"1.5MB / 2MB","BlockIO":"4KiB / 8KiB"}
            """);

        var container = Assert.Single(containers);
        Assert.Equal(134_217_728, container.MemoryUsedBytes);
        Assert.Equal(1_500_000, container.NetworkReceiveBytes);
        Assert.Equal(8_192, container.BlockWriteBytes);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ProtocolSessions_CountLocalServicePorts()
    {
        var sessions = LinuxSystemMetricsCollector.ParseProtocolSessions("""
            ESTAB 0 0 192.168.1.2:445 192.168.1.10:53124
            ESTAB 0 0 192.168.1.2:22 192.168.1.11:60000
            0 0 192.168.1.2:445 192.168.1.12:53125
            """);

        Assert.Equal(2, sessions.Single(item => item.Protocol == "smb").ActiveSessions);
        Assert.Equal(1, sessions.Single(item => item.Protocol == "ssh").ActiveSessions);
    }
}
