namespace FortOS.Cli.Tui.Pages;

/// <summary>Displays and operates the agent page.</summary>
public sealed class AgentPage : SelectableListPageBase
{
    /// <inheritdoc />
    public override string Title => "Agents (↑↓ select, s start, x stop)";

    /// <inheritdoc />
    protected override string Endpoint => "api/agents";

    /// <inheritdoc />
    protected override string TableTitle => "Agents";

    /// <inheritdoc />
    protected override string[] Columns => ["id", "name", "template", "status"];
}
