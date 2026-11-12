using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Meshsmith.App.Services;
using Meshsmith.App.ViewModels;
using Meshsmith.App.Views;

namespace Meshsmith.App;

public partial class MainWindow : Window
{
    private readonly SettingsStore? _store;
    private AppSettings _settings;

    public MainViewModel ViewModel { get; }

    /// <summary>Parameterless for the headless paths (<c>--shot</c>): they move the window off
    /// screen on purpose and must never overwrite the user's real geometry.</summary>
    public MainWindow() : this(null, null) { }

    public MainWindow(SettingsStore? store, AppSettings? settings)
    {
        App.Log("window ctor");
        InitializeComponent();

        _store = store;
        _settings = settings ?? AppSettings.Default;

        var uiContext = SynchronizationContext.Current ?? new SynchronizationContext();
        ViewModel = new MainViewModel(new WpfFileDialogService(this), new WorkerConversionService(),
                                      new MeshProbe(), uiContext, new DiagnosticService(App.LogFilePath),
                                      _store, _settings);
        DataContext = ViewModel;
        ViewModel.SettingsRequested += OnSettingsRequested;
        ApplyPlacement();
        App.Log("window initialized");
    }

    /// <summary>Runs before Show(), so the first frame is already in the right place.</summary>
    private void ApplyPlacement()
    {
        if (_settings.Window is not { } saved || saved.W <= 0 || saved.H <= 0) return;

        var desired = new Rect(saved.X, saved.Y, saved.W, saved.H);
        var rect = WindowPlacement.Clamp(desired, ScreenWorkArea.ForRect(desired));

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = rect.Left;
        Top = rect.Top;
        Width = rect.Width;
        Height = rect.Height;
        if (saved.Maximized) WindowState = WindowState.Maximized;
    }

    private void OnSettingsRequested(object? sender, EventArgs e)
    {
        var dialog = new SettingsDialog(new SettingsViewModel(_settings, new WpfFileDialogService(this)))
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true) return;      // Cancel reverts by never writing

        _settings = dialog.Accepted;
        if (_store?.Save(_settings) == false) App.Log("settings could not be saved");
        ViewModel.ApplySettings(_settings);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_store is null) return;   // headless: no persistence

        var bounds = WindowState == WindowState.Maximized ? RestoreBounds : new Rect(Left, Top, Width, Height);
        _settings = SessionSettings.ForClose(ViewModel.Preferences, bounds.Left, bounds.Top,
                                             bounds.Width, bounds.Height,
                                             WindowState == WindowState.Maximized);
        _store.Save(_settings);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            int dark = 1;
            DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
        }
        catch (Exception)
        {
            // A light title bar is cosmetic; never fail startup over it.
        }
    }

    private const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
