using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FortOS.Installer.Core.Models;
using FortOS.Installer.Core.Session;

namespace FortOS.Installer.Gui.ViewModels;

/// <summary>
/// 第 6 页:执行。绑定 InstallerSession,展示阶段进度与实时日志;
/// 失败时停留在本页并允许重试。
/// </summary>
public partial class InstallViewModel : ViewModelBase, IWizardPage
{
    private readonly Func<InstallerSession> _sessionFactory;
    private readonly Action<Action> _uiDispatch;
    private InstallerSession? _session;
    private InstallConfig? _config;
    private bool _started;

    /// <param name="uiDispatch">把回调封送到 UI 线程的委托;测试注入同步执行。</param>
    public InstallViewModel(Func<InstallerSession> sessionFactory, Action<Action>? uiDispatch = null)
    {
        _sessionFactory = sessionFactory;
        _uiDispatch = uiDispatch ?? (action => Avalonia.Threading.Dispatcher.UIThread.Post(action));
    }

    public override string Title => "Installing";

    public ObservableCollection<string> LogLines { get; } = [];

    [ObservableProperty]
    private InstallerPhase _phase = InstallerPhase.Idle;

    /// <summary>本地化的阶段标题(如「阶段:Copying」)。</summary>
    public string PhaseText => string.Format(L["install.phase"], Phase);

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isFailed;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public bool IsValid => false; // 执行页不可前进

    /// <summary>安装完成(成功)事件:向导前进到完成页。</summary>
    public event Action? Completed;

    /// <summary>启动安装(首次与重试共用)。</summary>
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
        IsFailed = false;
        ErrorMessage = string.Empty;
        LogLines.Clear();
        _config = config;

        _session = _sessionFactory();
        // 会话在后台线程执行:所有回调封送到 UI 线程(ObservableCollection 不允许跨线程变更)。
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
            // 配置校验等异常在 InstallerSession.RunAsync 的 try 块外抛出(设计使然);
            // 必须就地转为失败状态,否则页面永远停在「执行中」且异常变成未观察异常。
            IsFailed = true;
            ErrorMessage = ex.Message;
        }
        finally
        {
            _started = false; // 允许 Retry 重新执行
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

    /// <summary>失败后重启(设计稿 4:失败展示日志并允许重启重装)。</summary>
    [RelayCommand]
    private static void Reboot() => SystemControl.Reboot();

    // ObservableProperty(_phase) 生成器会调用此 partial 方法:同步阶段、标题与进度。
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
