using System.Text;
using GNAS.Core;

namespace GNAS.Modules.Share.Services;

/// <summary>WebDAV nginx 配置生成器。</summary>
public sealed class WebDavConfigGenerator
{
    /// <summary>生成 nginx location 配置块。</summary>
    public string Generate(IEnumerable<ShareDefinition> shares)
    {
        var sb = new StringBuilder();
        foreach (var share in shares.Where(s => s.Protocols.Contains("webdav", StringComparer.OrdinalIgnoreCase)))
        {
            ShareValidation.ValidateShare(share);
            sb.AppendLine($"location /webdav/{share.Name}/ {{");
            sb.AppendLine($"    alias {share.Path.TrimEnd('/')}/;");
            sb.AppendLine("    dav_methods PUT DELETE MKCOL COPY MOVE;");
            sb.AppendLine($"    dav_access user:{(share.ReadOnly ? "r" : "rw")} group:r all:r;");
            sb.AppendLine("}");
        }

        return sb.ToString();
    }
}
