namespace FortOS.Cli.Tui.Pages;

/// <summary>Displays and operates the service page.</summary>
public sealed class ServicePage : SelectableListPageBase
{
    /// <inheritdoc />
    public override string Title => "Services (↑↓ select, s start, x stop)";

    /// <inheritdoc />
    protected override string Endpoint => "api/services";

    /// <inheritdoc />
    protected override string TableTitle => "Services";

    /// <inheritdoc />
    protected override string[] Columns => ["id", "name", "status"];
}
