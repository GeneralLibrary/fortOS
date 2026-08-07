using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FortOS.Installer.Core.Models;
using FortOS.Installer.Core.Tools;

namespace FortOS.Installer.Gui.ViewModels;

/// <summary>
/// Page 2: disk layout (design spec 4). The system disk will be wiped (the red warning is expressed by the View);
/// the data disk supports a single disk or none for v1.
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
    /// Data disk layout options. The GUI only exposes "single disk / none"; RAID and LUKS are configured
    /// via install.yaml (headless path), so they are not shown here.
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

    /// <summary>Shows the data disk selection panel when the data disk mode is single.</summary>
    public bool ShowDataDiskPanel => DataMode == DataDiskMode.Single;

    /// <summary>Shows the size input when swap is a fixed size.</summary>
    public bool ShowSwapSize => SwapMode == SwapMode.Fixed;

    public bool IsBusy { get; private set; }

    public bool IsValid =>
        SelectedSystemDisk is not null &&
        (DataMode != DataDiskMode.Single || SelectedDataDisk is not null) &&
        SelectedDataDisk != SelectedSystemDisk &&
        (SwapMode != SwapMode.Fixed || long.TryParse(SwapSizeMiB, out var size) && size > 0) &&
        string.IsNullOrEmpty(Error);

    /// <summary>Loads the disk list (called when first entering the page; on failure it can be retried on the page).</summary>
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

            // Safety first: never auto-preselect the system disk. The system disk will be fully wiped (PartitionStep runs
            // sgdisk --zap-all); auto-selecting the first disk on a multi-disk machine could easily wipe a disk with data;
            // the user must explicitly select the target disk path, and only then does IsValid allow advancing.
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
