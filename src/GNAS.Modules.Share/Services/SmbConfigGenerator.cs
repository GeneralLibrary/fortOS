using System.Text;
using GNAS.Core;

namespace GNAS.Modules.Share.Services;

/// <summary>SMB configuration generator.</summary>
public sealed class SmbConfigGenerator
{
    /// <summary>Generate smb.conf content.</summary>
    public string Generate(IEnumerable<ShareDefinition> shares)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[global]");
        sb.AppendLine("   workgroup = WORKGROUP");
        sb.AppendLine("   server string = GNAS File Server");
        sb.AppendLine("   security = user");
        sb.AppendLine("   encrypt passwords = yes");
        sb.AppendLine("   server signing = mandatory");
        sb.AppendLine("   server min protocol = SMB2_10");
        sb.AppendLine("   server max protocol = SMB3_11");
        sb.AppendLine();

        foreach (var share in shares)
        {
            ShareValidation.ValidateShare(share);
            sb.AppendLine($"[{share.Name}]");
            sb.AppendLine($"   path = {share.Path}");
            sb.AppendLine($"   read only = {(share.ReadOnly ? "yes" : "no")}");
            sb.AppendLine("   browseable = yes");
            sb.AppendLine("   guest ok = no");
            sb.AppendLine("   vfs objects = recycle");
            sb.AppendLine("   recycle:repository = .recycle/%U");
            sb.AppendLine("   recycle:keeptree = yes");
            sb.AppendLine("   recycle:versions = yes");
            if (!string.IsNullOrWhiteSpace(share.Description))
            {
                sb.AppendLine($"   comment = {share.Description}");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }
}
