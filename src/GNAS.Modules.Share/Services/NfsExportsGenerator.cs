using System.Text;
using GNAS.Core;

namespace GNAS.Modules.Share.Services;

/// <summary>NFS exports 配置生成器。</summary>
public sealed class NfsExportsGenerator
{
    /// <summary>生成 /etc/exports 内容。</summary>
    public string Generate(IEnumerable<ShareDefinition> shares)
    {
        var sb = new StringBuilder();
        foreach (var share in shares.Where(s => s.Protocols.Length == 0 || s.Protocols.Contains("nfs", StringComparer.OrdinalIgnoreCase)))
        {
            ShareValidation.ValidateShare(share);
            var access = share.ReadOnly ? "ro" : "rw";
            sb.AppendLine($"{share.Path} *(sync,{access},all_squash,no_subtree_check)");
        }

        return sb.ToString();
    }
}
