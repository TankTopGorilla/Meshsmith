using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Meshsmith.App.Services;

namespace Meshsmith.App;

public partial class App : Application
{
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "meshsmith-app.log");

    /// <summary>The log the diagnostic bundle carries. One path, one file, no copies.</summary>
    public static string LogFilePath => LogPath;

    private const long LogLimitBytes = 2L * 1024 * 1024;

    /// <summary>Append, never wipe: a second instance used to erase the first one's evidence.</summary>
    public static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {message}\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A missing log line is never worth failing a conversion over.
        }
    }

    /// <summary>Start a new session in the log, capped so it cannot grow without bound.</summary>
    private static void StartLog()
    {
        try
        {
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > LogLimitBytes) File.Delete(LogPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Another instance holds it; keep going with whatever is there.
        }

        Log($"=== start {DateTime.Now:yyyy-MM-dd HH:mm:ss} (pid {Environment.ProcessId}) ===");
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        StartLog();

        // Conversions write into %TEMP%\meshsmith-<guid>. A cancelled run now removes its own
        // directory, but a crash or a kill leaves one behind for good; a day-old directory cannot
        // belong to a live conversion, so it is safe to sweep off the UI thread.
        _ = Task.Run(() =>
        {
            int removed = TempSweeper.Sweep(TimeSpan.FromHours(24));
            Log($"swept {removed} stale work directories");
        });
        base.OnStartup(e);

        Log("native loader");
        Meshsmith.Core.NativeLoader.Configure();
        Log("native loader done");

        DispatcherUnhandledException += (_, args) =>
        {
            Log("UNHANDLED " + args.Exception);
            args.Handled = false;
        };

        if (e.Args.Length >= 2 && e.Args[0] == "--shot")
        {
            CaptureShot(e.Args.Length >= 3 ? e.Args[2] : null, e.Args[1]);
            Shutdown();
            return;
        }

        if (e.Args.Length >= 2 && e.Args[0] == "--selftest")
        {
            try
            {
                var result = Meshsmith.Core.Pipeline.ConversionPipeline.Run(e.Args[1],
                    new Meshsmith.Core.Pipeline.ConversionOptions());
                var input = MeshPreview.FromStl(e.Args[1]);
                var output = MeshPreview.FromStep(result.StepPath);
                Log($"selftest faces={result.FaceCount} solids={result.SolidCount} " +
                    $"valid={result.IsValidSolid} volume={result.Volume:F3} " +
                    $"artifact={(result.Artifact.Ok ? "ok" : "mismatch")} " +
                    $"dev={result.SurfaceDeviation:F4} meshes={input.Children.Count}/{output.Children.Count}");
            }
            catch (Exception ex)
            {
                Log("SELFTEST FAILED " + ex);
                Environment.ExitCode = 1;
            }
            Shutdown();
            return;
        }

        var store = new SettingsStore(SettingsStore.DefaultPath, Log);
        var settings = store.Load();
        new MainWindow(store, settings).Show();
    }

    /// <summary>Runs a view-model task to completion while the dispatcher keeps pumping: its
    /// continuations need the UI thread, so blocking on it directly would deadlock.</summary>
    private void Pump(Task task)
    {
        var frame = new DispatcherFrame();
        task.ContinueWith(_ => frame.Continue = false, TaskScheduler.FromCurrentSynchronizationContext());
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();   // rethrow anything the task captured
    }

    /// <summary>Renders the window off-screen and saves a PNG.
    /// Used by the plan's visual verification step:
    ///   --shot out.png                 (empty shell)
    ///   --shot out.png fixtures/cube_10.stl (loaded + converted)</summary>
    private void CaptureShot(string? stlPath, string pngPath)
    {
        var window = new MainWindow
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            Width = 1440,
            Height = 900,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -24000,
            Top = -24000
        };
        window.Show();

        var viewModel = window.ViewModel;

        if (stlPath is not null)
        {
            Pump(viewModel.LoadFileAsync(stlPath));
            Pump(viewModel.ConvertAsync());
        }

        window.UpdateLayout();
        Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);

        var bitmap = new RenderTargetBitmap(1440, 900, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(pngPath);
        encoder.Save(stream);

        Log($"shot {pngPath} bytes={new FileInfo(pngPath).Length} " +
            $"valid={viewModel.IsValid} faces={viewModel.FacesText}");
    }
}
