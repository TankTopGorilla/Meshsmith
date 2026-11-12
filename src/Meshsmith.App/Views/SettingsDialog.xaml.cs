using System.Windows;
using Meshsmith.App.Services;
using Meshsmith.App.ViewModels;

namespace Meshsmith.App.Views;

/// <summary>The settings dialog is a thin shell: the view model owns the copy, the window owns only
/// the OK/Cancel answer.</summary>
public partial class SettingsDialog : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsDialog(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    /// <summary>The settings to persist. Only meaningful after ShowDialog() returned true.</summary>
    public AppSettings Accepted => _viewModel.Accept();

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
