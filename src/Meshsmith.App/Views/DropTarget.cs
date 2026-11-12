using System.Windows;
using System.Windows.Controls;
using Meshsmith.App.ViewModels;

namespace Meshsmith.App.Views;

/// <summary>Drag-and-drop helper shared by the source rail and the viewport.</summary>
internal static class DropTarget
{
    public static void Attach(UIElement element)
    {
        element.AllowDrop = true;
        element.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        element.Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
            if (element is not FrameworkElement { DataContext: MainViewModel viewModel }) return;
            _ = viewModel.LoadFileAsync(files[0]);   // fire and forget: the view model owns its errors
        };
    }
}
