using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FortOS.Installer.Core.Models;
using FortOS.Installer.Core.Session;

namespace FortOS.Installer.Gui.ViewModels;

/// <summary>
/// Execution page: shows the installation plan summary and the no-return warning at the top (the former confirmation page content);
/// after clicking "Begin installation" it shows phase progress and live logs on the same page;
/// on failure it stays on this page and allows retry.
/// </summary>
public partial class InstallViewModel : ViewModelBase, IWizardPage
{
    private readonly WelcomeViewModel _welcome;
    private readonly DiskLayoutViewModel _disk;
    private readonly NetworkViewModel _network;
    private readonly AccountViewModel _account;
    private readonly Func<InstallerSession> _sessionFactory;
    private readonly Action<Action> _uiDispatch;
    private InstallerSession? _session;
    private InstallConfig? _config;
    private bool _started;

    /// <param name="uiDispatch">A delegate that marshals callbacks to the UI thread; tests inject synchronous execution.</param>
    public InstallViewModel(
        WelcomeViewModel welcome,
        DiskLayoutViewModel disk,
        NetworkViewModel network,
        AccountViewModel account,
        Func<InstallerSession> sessionFactory,
        Action<Action>? uiDispatch = null)
    {
        _welcome = welcome;
        _disk = disk;
        _network = network;
        _account = account;
        _sessionFactory = sessionFactory;
        _uiDispatch = uiDispatch ?? (action => Avalonia.Threading.Dispatcher.UIThread.Post(action));
        foreach (var page in new IWizardPage[] { welcome, disk, network, account })
        {
            page.IsValidChanged += (_, _) => OnPropertyChanged(nameof(Summary));
        }
    }

    public override string Title => "Review & install";

    public ObservableCollection<string> LogLines { get; } = [];

    [ObservableProperty]
    private InstallerPhase _phase = InstallerPhase.Idle;

    /// <summary>Localized phase title (e.g. "Phase: Copying").</summary>
    public string PhaseText => string.Format(L["install.phase"], Phase);

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isFailed;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>Shows the plan summary before installation starts; switches to the progress view once execution begins.</summary>
    [ObservableProperty]
    private bool _isPlanning = true;

    /// <summary>Installation plan summary (dynamically generated from the input of the four preceding pages).</summary>
    public string Summary => BuildSummary();

    public bool IsValid => false; // the execution page cannot advance

    /// <summary>Installation complete (success) event: the wizard advances to the completion page.</summary>
    public event Action? Completed;

    /// <summary>Starts installation (shared by first run and retry).</summary>
    [RelayCommand]
    private async Task StartAsync(InstallConfig? config)
    {
        if (_started && !IsFailed)
        {
            return;
        }
        if (config is null)
        {
            IsFailed = true;
            ErrorMessage = "Internal error: no install configuration.";
            return;
        }

        _started = true;
        IsPlanning = false;
        IsFailed = false;
        ErrorMessage = string.Empty;
        LogLines.Clear();
        _config = config;

        _session = _sessionFactory();
        // The session runs on a background thread: all callbacks are marshaled to the UI thread (ObservableCollection does not allow cross-thread changes).
        _session.PhaseChanged += phase => _uiDispatch(() => Phase = phase);
        _session.StepProgress += p => _uiDispatch(() => OnStepProgress(p));
        _session.LogEntryAdded += e => _uiDispatch(() => OnLogEntryAdded(e));

        try
        {
            var result = await _session.RunAsync(config, CancellationToken.None).ConfigureAwait(true);

            if (result.Success)
            {
                Progress = 100;
                Completed?.Invoke();
            }
            else
            {
                IsFailed = true;
                ErrorMessage = result.FailedStep is null
                    ? result.ErrorMessage ?? "Unknown error."
                    : $"Step '{result.FailedStep}' failed: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            // Exceptions such as configuration validation are thrown outside the try block of InstallerSession.RunAsync (by design);
            // they must be converted to a failed state here, otherwise the page stays on "running" forever and the exception becomes an unobserved one.
            IsFailed = true;
            ErrorMessage = ex.Message;
        }
        finally
        {
            _started = false; // allows Retry to run again
        }
    }

    [RelayCommand]
    private void Retry()
    {
        if (_config is not null)
        {
            StartCommand.Execute(_config);
        }
    }

    /// <summary>Reboots after failure (design spec 4: on failure the logs are shown and a reboot reinstall is allowed).</summary>
    [RelayCommand]
    private static void Reboot() => SystemControl.Reboot();

    // The ObservableProperty(_phase) generator calls this partial method: it syncs phase, title, and progress.
    partial void OnPhaseChanged(InstallerPhase value)
    {
        OnPropertyChanged(nameof(PhaseText));
        Progress = PhaseToProgress(value);
    }

    private void OnStepProgress(InstallStepProgress progress)
        => Progress = PhaseToProgress(Phase) + progress.Percent / 100.0;

    private void OnLogEntryAdded(InstallLogEntry entry)
    {
        LogLines.Add($"{entry.Timestamp:HH:mm:ss} [{entry.Level}] {entry.Message}");
        if (LogLines.Count > 500)
        {
            LogLines.RemoveAt(0);
        }
    }

    private string BuildSummary()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{L["confirm.summary.systemDisk"]}{_disk.SelectedSystemDisk?.Path} — {_disk.RootFs}");
        sb.AppendLine($"{L["confirm.summary.swap"]}{DescribeSwap()}");
        sb.AppendLine($"{L["confirm.summary.data"]}{DescribeData()}");
        sb.AppendLine($"{L["confirm.summary.network"]}{DescribeNetwork()}");
        sb.AppendLine($"{L["confirm.summary.hostname"]}{_network.Hostname}");
        sb.AppendLine($"{L["confirm.summary.admin"]}{_account.Username} ({_account.Timezone})");
        sb.AppendLine($"{L["confirm.summary.locale"]}{_welcome.Language} / keyboard {_welcome.Keyboard}");
        return sb.ToString();
    }

    private string DescribeSwap() => _disk.SwapMode switch
    {
        SwapMode.Off => "off",
        SwapMode.Fixed => $"{_disk.SwapSizeMiB} MiB",
        _ => "auto (RAM size)",
    };

    private string DescribeData() => _disk.DataMode switch
    {
        DataDiskMode.Single => $"{_disk.SelectedDataDisk?.Path} — {_disk.DataFs} (label {_disk.DataLabel})",
        _ => "not configured (post-install)",
    };

    private string DescribeNetwork() => _network.Mode switch
    {
        NetworkMode.Static => $"static {_network.Address}{(string.IsNullOrEmpty(_network.Gateway) ? "" : $" gw {_network.Gateway}")}",
        _ => "DHCP",
    };

    private static double PhaseToProgress(InstallerPhase phase) => phase switch
    {
        InstallerPhase.CollectInfo => 2,
        InstallerPhase.Partitioning => 12,
        InstallerPhase.Formatting => 25,
        InstallerPhase.Copying => 55,
        InstallerPhase.Configuring => 78,
        InstallerPhase.Bootloader => 92,
        InstallerPhase.Finalize => 98,
        InstallerPhase.Done => 100,
        _ => 0,
    };
}
