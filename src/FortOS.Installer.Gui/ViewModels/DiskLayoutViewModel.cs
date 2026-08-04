using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FortOS.Installer.Core.Models;
using FortOS.Installer.Core.Tools;

namespace FortOS.Installer.Gui.ViewModels;

/// <summary>
/// 第 2 页:磁盘布局(设计稿 4)。系统盘将被清盘(红色警示由 View 表达);
/// 数据盘 v1 支持单盘或暂不配置。
/// </summary>
public partial class DiskLayoutViewModel : ViewModelBase, IWizardPage
{
    private readonly LsblkTool _lsblk;
    private bool _loaded;

    public DiskLayoutViewModel(LsblkTool lsblk)
    {
        _lsblk = lsblk;
    }

    public override string Title => "Disk layout";

    public ObservableCollection<DiskInfo> Disks { get; } = [];

    public IReadOnlyList<RootFileSystem> RootFsOptions { get; } = Enum.GetValues<RootFileSystem>();

    public IReadOnlyList<SwapMode> SwapOptions { get; } = Enum.GetValues<SwapMode>();

    /// <summary>
    /// 数据盘布局选项。GUI 仅暴露「单盘 / 暂不配置」;RAID 与 LUKS 通过
    /// install.yaml(headless 路径)配置,故此处不显示。
    /// </summary>
    public IReadOnlyList<DataDiskMode> DataModeOptions { get; } = [DataDiskMode.None, DataDiskMode.Single];

    public IReadOnlyList<DataFileSystem> DataFsOptions { get; } = Enum.GetValues<DataFileSystem>();

    [ObservableProperty]
    private DiskInfo? _selectedSystemDisk;

    [ObservableProperty]
    private RootFileSystem _rootFs = RootFileSystem.Btrfs;

    [ObservableProperty]
    private SwapMode _swapMode = SwapMode.Auto;

    [ObservableProperty]
    private string _swapSizeMiB = "4096";

    [ObservableProperty]
    private DataDiskMode _dataMode = DataDiskMode.None;

    [ObservableProperty]
    private DiskInfo? _selectedDataDisk;

    [ObservableProperty]
    private DataFileSystem _dataFs = DataFileSystem.Btrfs;

    [ObservableProperty]
    private string _dataLabel = "FORTOS_DATA";

    [ObservableProperty]
    private string _error = string.Empty;

    /// <summary>数据盘单盘模式时显示数据盘选择面板。</summary>
    public bool ShowDataDiskPanel => DataMode == DataDiskMode.Single;

    /// <summary>swap 为固定大小时显示大小输入。</summary>
    public bool ShowSwapSize => SwapMode == SwapMode.Fixed;

    public bool IsBusy { get; private set; }

    public bool IsValid =>
        SelectedSystemDisk is not null &&
        (DataMode != DataDiskMode.Single || SelectedDataDisk is not null) &&
        SelectedDataDisk != SelectedSystemDisk &&
        (SwapMode != SwapMode.Fixed || long.TryParse(SwapSizeMiB, out var size) && size > 0) &&
        string.IsNullOrEmpty(Error);

    /// <summary>加载磁盘列表(首次进入页面时调用;失败可在页内重试)。</summary>
    public async Task LoadAsync()
    {
        if (_loaded || IsBusy)
        {
            return;
        }
        IsBusy = true;
        OnPropertyChanged(nameof(IsBusy));
        try
        {
            var disks = await _lsblk.ListDisksAsync(CancellationToken.None).ConfigureAwait(true);
            Disks.Clear();
            foreach (var disk in disks.Where(d => !d.IsReadOnly))
            {
                Disks.Add(disk);
            }

            // 傻瓜式流程:加载后自动选中第一块盘,用户可改;无盘时保持未选。
            if (Disks.Count > 0 && SelectedSystemDisk is null)
            {
                SelectedSystemDisk = Disks[0];
            }

            Error = Disks.Count == 0 ? L["disk.noDisks"] : string.Empty;
            _loaded = true;
        }
        catch (Exception ex)
        {
            Error = $"Failed to enumerate disks: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsBusy));
            RaiseIsValidChanged();
        }
    }

    partial void OnSelectedSystemDiskChanged(DiskInfo? value) => RaiseIsValidChanged();

    partial void OnSelectedDataDiskChanged(DiskInfo? value) => RaiseIsValidChanged();

    partial void OnDataModeChanged(DataDiskMode value)
    {
        OnPropertyChanged(nameof(ShowDataDiskPanel));
        RaiseIsValidChanged();
    }

    partial void OnSwapModeChanged(SwapMode value)
    {
        OnPropertyChanged(nameof(ShowSwapSize));
        RaiseIsValidChanged();
    }

    partial void OnSwapSizeMiBChanged(string value) => RaiseIsValidChanged();

    partial void OnErrorChanged(string value) => RaiseIsValidChanged();

}
