using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Meshsmith.App.ViewModels;
using Meshsmith.Core.Pipeline;
using HelixToolkit.Wpf;

namespace Meshsmith.App.Views;

public partial class ViewportPanel : UserControl
{
    private MainViewModel? _viewModel;
    private ModelVisual3D? _sceneVisual;
    private LinesVisual3D? _minorGrid;
    private LinesVisual3D? _majorGrid;
    private bool _gridVisible = true;

    /// <summary>The padded model frame used by the last ReplaceScene, so Fit reframes the part
    /// rather than the (much larger) ground grid. Null when no model is loaded.</summary>
    private Rect3D? _modelFrame;

    /// <summary>Increments per ShowAsync call; only the newest result may touch the scene.</summary>
    private int _showGeneration;

    public ViewportPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        DropTarget.Attach(this);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.SourceLoaded -= OnSourceLoaded;
            _viewModel.SourceCleared -= OnSourceCleared;
            _viewModel.ResultReady -= OnResultReady;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = e.NewValue as MainViewModel;

        if (_viewModel is not null)
        {
            _viewModel.SourceLoaded += OnSourceLoaded;
            _viewModel.SourceCleared += OnSourceCleared;
            _viewModel.ResultReady += OnResultReady;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            SetGridVisible(_viewModel.ShowGrid);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_viewModel is null) return;
        if (e.PropertyName is nameof(MainViewModel.ShowGrid) or "") SetGridVisible(_viewModel.ShowGrid);
    }

    private void OnSourceLoaded(object? sender, string path) => ShowAsync(MeshPreview.FromStl, path);

    private void OnResultReady(object? sender, ConversionResult result) =>
        ShowAsync(MeshPreview.FromStep, result.StepPath);

    private void OnSourceCleared(object? sender, EventArgs e)
    {
        _showGeneration++;               // a preview still decoding must not land on an empty viewport
        _modelFrame = null;
        if (_sceneVisual is not null)
        {
            Viewport.Children.Remove(_sceneVisual);
            _sceneVisual = null;
        }
        EmptyState.Visibility = Visibility.Visible;
        RenderError.Visibility = Visibility.Collapsed;
    }

    private async void ShowAsync(Func<string, Model3DGroup> factory, string path)
    {
        int generation = ++_showGeneration;
        try
        {
            var group = await Task.Run(() => factory(path));

            // A newer load arrived while this one was decoding: applying it now would show the
            // older model over the newer one. Only the newest result may reach the scene.
            if (generation != _showGeneration) return;

            ReplaceScene(group);
            RenderError.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            if (generation != _showGeneration) return;
            RenderErrorText.Text = "Preview failed: " + ex.Message;
            RenderError.Visibility = Visibility.Visible;
        }
    }

    private void ReplaceScene(Model3DGroup group)
    {
        if (_sceneVisual is not null) Viewport.Children.Remove(_sceneVisual);

        _sceneVisual = new ModelVisual3D { Content = group };
        Viewport.Children.Add(_sceneVisual);
        EmptyState.Visibility = Visibility.Collapsed;

        var bounds = group.Bounds;
        double span = Math.Max(Math.Max(bounds.SizeX, bounds.SizeY), bounds.SizeZ);
        if (span <= 0 || double.IsInfinity(span) || double.IsNaN(span)) span = 100;

        BuildGround(span, new Point3D(bounds.X + bounds.SizeX / 2,
                                      bounds.Y + bounds.SizeY / 2,
                                      bounds.Z - span * 0.04));

        // Frame the model itself, not the grid, and leave margin: ZoomExtents() fits the rect's
        // bounding sphere to the viewport width, so a tall model can still overflow top and bottom.
        // No animation: the camera must be framed by the time this returns (the --shot harness renders right after).
        var frame = new Rect3D(bounds.X - bounds.SizeX * 0.15, bounds.Y - bounds.SizeY * 0.15,
                               bounds.Z - bounds.SizeZ * 0.15,
                               bounds.SizeX * 1.30, bounds.SizeY * 1.30, bounds.SizeZ * 1.30);
        _modelFrame = frame;
        Viewport.ZoomExtents(frame, 0);
    }

    /// <summary>Two screen-space line sets: a faint minor grid and a brighter major grid every fifth line.</summary>
    private void BuildGround(double span, Point3D center)
    {
        RemoveGround();

        const int steps = 10;
        double step = span / steps;
        double half = step * steps;

        var minor = new Point3DCollection();
        var major = new Point3DCollection();

        for (int i = -steps; i <= steps; i++)
        {
            double offset = i * step;
            var target = i % 5 == 0 ? major : minor;
            target.Add(new Point3D(center.X + offset, center.Y - half, center.Z));
            target.Add(new Point3D(center.X + offset, center.Y + half, center.Z));
            target.Add(new Point3D(center.X - half, center.Y + offset, center.Z));
            target.Add(new Point3D(center.X + half, center.Y + offset, center.Z));
        }

        _minorGrid = new LinesVisual3D
        {
            Points = minor,
            Color = (Color)FindResource("GridMinorColor"),
            Thickness = 0.6
        };
        _majorGrid = new LinesVisual3D
        {
            Points = major,
            Color = (Color)FindResource("GridMajorColor"),
            Thickness = 1.0
        };

        if (_gridVisible)
        {
            Viewport.Children.Add(_minorGrid);
            Viewport.Children.Add(_majorGrid);
        }
    }

    private void RemoveGround()
    {
        if (_minorGrid is not null) Viewport.Children.Remove(_minorGrid);
        if (_majorGrid is not null) Viewport.Children.Remove(_majorGrid);
        _minorGrid = null;
        _majorGrid = null;
    }

    private void SetGridVisible(bool visible)
    {
        _gridVisible = visible;
        if (_minorGrid is null || _majorGrid is null) return;

        bool attached = Viewport.Children.Contains(_minorGrid);
        if (visible && !attached)
        {
            Viewport.Children.Add(_minorGrid);
            Viewport.Children.Add(_majorGrid);
        }
        else if (!visible && attached)
        {
            Viewport.Children.Remove(_minorGrid);
            Viewport.Children.Remove(_majorGrid);
        }
    }

    /// <summary>Reframe the model with the same padded rect the load used. The no-rect overload
    /// includes the ground grid, which shrinks the part to roughly half the intended size.</summary>
    private void OnFit(object sender, RoutedEventArgs e)
    {
        if (_modelFrame is { } frame) Viewport.ZoomExtents(frame, 0);
        else Viewport.ZoomExtents(220);
    }
}
