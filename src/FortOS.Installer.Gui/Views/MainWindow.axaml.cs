using Avalonia.Controls;
using FortOS.Installer.Gui.ViewModels;

namespace FortOS.Installer.Gui.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // Load the disk list when the window is shown (precondition data for page 2 of design spec 4).
        Opened += (_, _) => (DataContext as MainWindowViewModel)?.LoadDisksCommand.Execute(null);
    }
}
