using System.Windows;
using System.Windows.Controls;

namespace Flow.Launcher.Plugin.ProtonPass.Settings;

public partial class ProtonPassSettingsPanel : UserControl
{
    private readonly SettingsViewModel _viewModel;

    public ProtonPassSettingsPanel(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }
}
