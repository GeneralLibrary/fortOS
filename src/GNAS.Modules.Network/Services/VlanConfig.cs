using GNAS.Core;

namespace GNAS.Modules.Network.Services;

/// <summary>VLAN 配置助手。</summary>
public sealed class VlanConfig
{
    private readonly IProcessManager processManager;

    /// <summary>创建 VLAN 配置助手。</summary>
    public VlanConfig(IProcessManager processManager)
    {
        this.processManager = processManager;
    }

    /// <summary>创建 VLAN 接口。</summary>
    public Task<CommandResult> AddAsync(string parentInterface, int vlanId, string vlanInterface, CancellationToken ct)
    {
        Validate(parentInterface);
        Validate(vlanInterface);
        if (vlanId is <= 0 or > 4094)
        {
            throw new ArgumentOutOfRangeException(nameof(vlanId), "VLAN ID 必须在 1-4094。");
        }

        return processManager.ExecuteCommandAsync(new ProcessStartConfig
        {
            ExecutablePath = "ip",
            Arguments = $"link add link {parentInterface} name {vlanInterface} type vlan id {vlanId}"
        }, ct);
    }

    private static void Validate(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Contains('\n') || value.Contains('\r') || value.Contains(';') || value.Contains(' '))
        {
            throw new ArgumentException("接口名称非法。", nameof(value));
        }
    }
}
