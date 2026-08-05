using System.Text;
using FortOS.Core;
using FortOS.Modules.Storage;
using FortOS.Platform;
using Microsoft.AspNetCore.Mvc;

namespace FortOS.Api.Controllers;

/// <summary>Disk controller.</summary>
[Route("api/disks")]
public sealed class DisksController : FortOSControllerBase
{
    private readonly StorageModule storage;

    /// <summary>Initializes the disk controller.</summary>
    public DisksController(StorageModule storage) => this.storage = storage;

    /// <summary>List disks.</summary>
    [HttpGet]
    public Task<IReadOnlyList<DiskInfo>> List(CancellationToken ct) => storage.ListDisksAsync(ct);

    /// <summary>Get disk by query parameters.</summary>
    [HttpGet("detail")]
    public Task<DiskInfo> GetByQuery([FromQuery] string path, CancellationToken ct) => storage.GetDiskDetailAsync(path, ct);

    /// <summary>Get disk by encoded path.</summary>
    [HttpGet("{encodedPath}")]
    public Task<DiskInfo> Get(string encodedPath, CancellationToken ct) => storage.GetDiskDetailAsync(DecodePath(encodedPath), ct);

    /// <summary>Execute SMART check.</summary>
    [HttpPost("smart-check")]
    public async Task<SmartData> Smart([FromBody] PathRequest request, [FromServices] IDiskManager disks, CancellationToken ct) => await disks.GetSmartDataAsync(request.Path, ct).ConfigureAwait(false);

    /// <summary>List active MD RAID arrays.</summary>
    [HttpGet("raids")]
    public Task<IReadOnlyList<RaidMetrics>> Raids(CancellationToken ct) => storage.ListRaidsAsync(ct);

    /// <summary>Query block-device status (filesystem / mount point) for an arbitrary device such as an md array.</summary>
    [HttpGet("device-status")]
    public Task<DeviceStatus> DeviceStatus([FromQuery] string path, CancellationToken ct) => storage.GetDeviceStatusAsync(path, ct);

    /// <summary>
    /// Whether the RAID tooling (mdadm) is installed on this host. The dashboard
    /// uses this to guide the user through installation when it is missing.
    /// </summary>
    [HttpGet("raid-capability")]
    public object RaidCapability() => new
    {
        available = PlatformCapabilities.SupportsHardwareRaid,
        tool = "mdadm",
    };

    /// <summary>
    /// Create a RAID array from the selected disks. Destructive: <see cref="CreateRaidRequest.Confirm"/>
    /// must be true, otherwise the request is rejected.
    /// </summary>
    [HttpPost("raids")]
    public async Task<object> CreateRaid([FromBody] CreateRaidRequest request, CancellationToken ct)
    {
        if (!request.Confirm)
        {
            throw new ArgumentException("Creating a RAID array erases disk data; explicit confirmation is required.", nameof(request));
        }

        return await storage.CreateRaidAsync(request.Level, request.DiskPaths, ct).ConfigureAwait(false);
    }

    /// <summary>Format a block device (e.g. a freshly created RAID array). Destructive.</summary>
    [HttpPost("format")]
    public Task Format([FromBody] FormatRequest request, CancellationToken ct) => storage.FormatAsync(request.Device, request.FsType, ct);

    /// <summary>Mount a formatted device and persist the mount to /etc/fstab.</summary>
    [HttpPost("mount")]
    public Task Mount([FromBody] MountRequest request, CancellationToken ct) => storage.MountAsync(request.Device, request.MountPoint, request.FsType, ct);

    /// <summary>Unmount a filesystem and remove its /etc/fstab entry.</summary>
    [HttpPost("unmount")]
    public Task Unmount([FromBody] UnmountRequest request, CancellationToken ct) => storage.UnmountAsync(request.MountPoint, ct);

    private static string DecodePath(string value)
    {
        var url = Uri.UnescapeDataString(value);
        try
        {
            var padded = url.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        }
        catch (FormatException)
        {
            return url;
        }
    }
}
