namespace FortOS.Installer.Core.Tools;

/// <summary>
/// 系统工具适配器标记接口。所有适配器以结构化输出(JSON)或确定性参数拼装为边界,
/// 禁止文本模糊解析(设计稿 6)。
/// </summary>
public interface ITool
{
    /// <summary>工具名,用于日志与 UI 展示。</summary>
    string Name { get; }
}
