using System.Text;
using GNAS.Core;

namespace GNAS.Modules.Share.Services;

/// <summary>FTP configuration generator.</summary>
public sealed class FtpConfigGenerator
{
    /// <summary>Generate vsftpd configuration.</summary>
    public string Generate(IEnumerable<ShareDefinition> shares)
    {
        foreach (var share in shares)
        {
            ShareValidation.ValidateShare(share);
        }

        var sb = new StringBuilder();
        sb.AppendLine("listen=YES");
        sb.AppendLine("anonymous_enable=NO");
        sb.AppendLine("local_enable=YES");
        sb.AppendLine("write_enable=YES");
        sb.AppendLine("chroot_local_user=YES");
        return sb.ToString();
    }
}
