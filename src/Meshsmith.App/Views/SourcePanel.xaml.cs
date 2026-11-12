using System.Windows.Controls;
using System.Windows.Input;

namespace Meshsmith.App.Views;

public partial class SourcePanel : UserControl
{
    public SourcePanel()
    {
        InitializeComponent();
        DropTarget.Attach(this);
    }

    private void OnBrowse(object sender, MouseButtonEventArgs e)
    {
        // Once a model is loaded the card is a status display: clicking it to read it must not
        // replace the model. The top bar's Open STL stays available.
        if (DataContext is ViewModels.MainViewModel viewModel && !viewModel.HasSource)
            viewModel.OpenCommand.Execute(null);
    }
}
