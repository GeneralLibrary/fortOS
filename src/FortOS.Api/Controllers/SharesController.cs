using FortOS.Core;
using FortOS.Modules.Share;
using Microsoft.AspNetCore.Mvc;

namespace FortOS.Api.Controllers;

/// <summary>Share controller.</summary>
[Route("api/shares")]
public sealed class SharesController : FortOSControllerBase
{
    private readonly ShareModule shares;

    /// <summary>Initializes the share controller.</summary>
    public SharesController(ShareModule shares) => this.shares = shares;

    /// <summary>List shares.</summary>
    [HttpGet]
    public Task<IReadOnlyList<ShareDefinition>> List(CancellationToken ct) => shares.ListSharesAsync(ct);

    /// <summary>Create share.</summary>
    [HttpPost]
    public Task<ShareDefinition> Create([FromBody] ShareDefinition share, CancellationToken ct) => shares.CreateShareAsync(share, ct);

    /// <summary>Delete share.</summary>
    [HttpDelete("{id}")]
    public async Task<object> Delete(string id, CancellationToken ct)
    {
        await shares.DeleteShareAsync(id, ct).ConfigureAwait(false);
        return new { success = true, shareId = id };
    }
}
