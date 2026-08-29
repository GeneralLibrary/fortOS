using FortOS.Api.Authorization;
using FortOS.Api.Middleware;
using FortOS.Api.Services;
using FortOS.Core;
using FortOS.Security.Models;
using Microsoft.AspNetCore.Mvc;

namespace FortOS.Api.Controllers;

/// <summary>远程访问控制器(P0-3 免穿透):Tailscale 状态查询与启停。</summary>
[Route("api/remote")]
public sealed class RemoteController(RemoteAccessService remote) : FortOSControllerBase
{
    /// <summary>查询远程访问状态(是否启用/已安装/已登录/设备名/IP)。</summary>
    [RequiresCapability("remote:access", NasDataLevel.Personal)]
    [HttpGet]
    public Task<RemoteStatus> Status(CancellationToken ct) => remote.GetStatusAsync(ct);

    /// <summary>启用远程访问(Tailscale 连接)。</summary>
    [RequiresCapability("remote:access", NasDataLevel.Personal)]
    [HttpPost("enable")]
    public Task<RemoteStatus> Enable(CancellationToken ct) => remote.EnableAsync(ct);

    /// <summary>禁用远程访问(Tailscale 断开)。</summary>
    [RequiresCapability("remote:access", NasDataLevel.Personal)]
    [HttpPost("disable")]
    public Task<RemoteStatus> Disable(CancellationToken ct) => remote.DisableAsync(ct);
}
