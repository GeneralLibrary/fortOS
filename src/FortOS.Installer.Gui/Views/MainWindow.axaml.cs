using Avalonia.Controls;
using FortOS.Installer.Gui.ViewModels;

namespace FortOS.Installer.Gui.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // 窗口显示时加载磁盘列表(设计稿 4 第 2 页的前置数据)。
        Opened += (_, _) => (DataContext as MainWindowViewModel)?.LoadDisksCommand.Execute(null);
    }
}
