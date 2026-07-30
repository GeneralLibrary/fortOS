using GORT.Core;

namespace GORT.Modules.Network.Services;

/// <summary>VLAN configuration helper.</summary>
public sealed class VlanConfig
{
    private readonly IProcessManager processManager;

    /// <summary>Create the VLAN configuration helper.</summary>
    public VlanConfig(IProcessManager processManager)
    {
        this.processManager = processManager;
    }

    /// <summary>Create a VLAN interface.</summary>
    public Task<CommandResult> AddAsync(string parentInterface, int vlanId, string vlanInterface, CancellationToken ct)
    {
        Validate(parentInterface);
        Validate(vlanInterface);
        if (vlanId is <= 0 or > 4094)
        {
            throw new ArgumentOutOfRangeException(nameof(vlanId), "VLAN ID must be between 1 and 4094.");
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
            throw new ArgumentException("Invalid interface name.", nameof(value));
        }
    }
}
