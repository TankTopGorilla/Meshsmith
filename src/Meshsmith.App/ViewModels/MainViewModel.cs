using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using Meshsmith.App.Mvvm;
using Meshsmith.App.Services;
using Meshsmith.Core.Pipeline;

namespace Meshsmith.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IFileDialogService _dialogs;
    private readonly IConversionService _converter;
    private readonly IMeshProbe _probe;
    private readonly IDiagnosticService? _diagnostics;
    private readonly SynchronizationContext? _ui;
    private readonly SettingsStore? _settings;
    private AppSettings _preferences;
    private string _toleranceInput = "";
    private bool _isRecentOpen;

    private const string ConversionFailedTitle = "CONVERSION FAILED";
    private const string CouldNotOpenTitle = "COULD NOT OPEN FILE";

    private string? _sourcePath;
    private MeshInfo? _sourceInfo;
    private ConversionResult? _result;
    private FaceMix _mix = FaceMix.Empty;
    private string _stage = "";
    private double _progress;
    private bool _isBusy;

    /// <summary>How many operations hold the shell at once: a load can overlap a conversion (it
    /// supersedes one, or fails while one is still running). Whichever finishes first must not
    /// report the other idle — Convert's enabled state and the Cancel button hang off this.</summary>
    private int _busyOperations;
    private string? _error;
    private string _errorTitle = ConversionFailedTitle;
    private string? _feedback;
    private string? _diagnosticPath;
    private bool _showGrid = true;
    private bool _showAxes = true;

    /// <summary>Turns progress messages and worker beats into a line that moves even when the
    /// percentage does not, so a long sew reads as "2:13 · 97% CPU" rather than a frozen bar.</summary>
    private readonly ActivityClock _activity = new();
    private Timer? _ticker;

    /// <summary>The run currently in flight, if any. Cancelling it is what the Cancel button and a
    /// superseding load do; the pipeline observes the token between stages and per region.</summary>
    private CancellationTokenSource? _conversion;

    /// <summary>Bumped as soon as a file is picked, before the probe runs: a slow probe for a file
    /// the user has already replaced must not overwrite the newer selection.</summary>
    private int _loadGeneration;

    /// <summary>Bumped only when the source really changes. A conversion from an older generation
    /// describes a file that is no longer on screen, so its result must be dropped.</summary>
    private int _generation;

    public MainViewModel(IFileDialogService dialogs, IConversionService converter, IMeshProbe probe,
                         SynchronizationContext? uiContext = null, IDiagnosticService? diagnostics = null,
                         SettingsStore? settings = null, AppSettings? preferences = null)
    {
        _dialogs = dialogs;
        _converter = converter;
        _probe = probe;
        _diagnostics = diagnostics;
        _ui = uiContext;
        _settings = settings;
        _preferences = preferences ?? AppSettings.Default;

        // A restart shows what the last session had open, newest first.
        foreach (var entry in _preferences.Recent) RecentFiles.Add(entry);

        OpenCommand = new AsyncRelayCommand(_ => OpenAsync(), _ => !IsBusy);
        ConvertCommand = new AsyncRelayCommand(_ => ConvertAsync(), _ => CanConvert);
        CancelCommand = new RelayCommand(_ => Cancel(), _ => CanCancel);
        SaveStepCommand = new RelayCommand(_ => Save(_result?.StepPath), _ => CanExport);
        SaveIgesCommand = new RelayCommand(_ => Save(_result?.IgesPath), _ => CanExport);
        ClearCommand = new RelayCommand(_ => Clear(), _ => HasSource && !IsBusy);
        SaveDiagnosticBundleCommand = new RelayCommand(_ => SaveDiagnosticBundle(), _ => HasDiagnostic);
        ReRunCommand = new AsyncRelayCommand(_ => ConvertAsync(), _ => CanRerun);
        SettingsCommand = new RelayCommand(_ => SettingsRequested?.Invoke(this, EventArgs.Empty));
        OpenRecentCommand = new AsyncRelayCommand(p => OpenRecentAsync(p as string),
                                                  p => !IsBusy && p is string);
    }

    /// <summary>The viewport listens to these; the view model never touches WPF 3D.</summary>
    public event EventHandler<string>? SourceLoaded;
    public event EventHandler? SourceCleared;
    public event EventHandler<ConversionResult>? ResultReady;

    /// <summary>Raised when the user asks for the settings dialog. The view owns the window; the
    /// view model owns the state it comes back with.</summary>
    public event EventHandler? SettingsRequested;

    public ObservableCollection<string> Warnings { get; } = new();

    /// <summary>The five pipeline stages, in execution order. Drives the checklist in the source rail.</summary>
    public ObservableCollection<PipelineStage> Stages { get; } = new()
    {
        new PipelineStage("Read mesh"),
        new PipelineStage("Recognise surfaces"),
        new PipelineStage("Build faces"),
        new PipelineStage("Sew solid"),
        new PipelineStage("Validate")
    };

    private static readonly string[] StageKeys = { "reading", "recognising", "building", "sewing", "validating" };
    private int _stageIndex = -1;

    public AsyncRelayCommand OpenCommand { get; }
    public AsyncRelayCommand ConvertCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand SaveStepCommand { get; }
    public RelayCommand SaveIgesCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand SaveDiagnosticBundleCommand { get; }
    public AsyncRelayCommand ReRunCommand { get; }
    public RelayCommand SettingsCommand { get; }        // raises SettingsRequested
    public AsyncRelayCommand OpenRecentCommand { get; } // parameter: the full path
    public ObservableCollection<string> RecentFiles { get; } = new();

    // ── preferences ──────────────────────────────────────────────────
    /// <summary>Free text, millimetres. Empty or unparseable means "derive it from the model".</summary>
    public string ToleranceInput
    {
        get => _toleranceInput;
        set => Set(ref _toleranceInput, value);
    }

    /// <summary>Re-run exists only once there is something to re-run: a result or a failure.</summary>
    public bool CanRerun => (_result is not null || _error is not null) && HasSource && !IsBusy;

    /// <summary>Fallback only: the popup binds IsOpen to its toggle with ElementName, so this
    /// carries no state unless that binding fails the visual check.</summary>
    public bool IsRecentOpen
    {
        get => _isRecentOpen;
        set => Set(ref _isRecentOpen, value);
    }

    /// <summary>The dialog's answer, applied to everything the next run reads. Nothing else on the
    /// view model changes, so an open result stays on screen after OK.</summary>
    public void ApplySettings(AppSettings settings) => _preferences = settings;

    /// <summary>The live preferences. The window saves this on close, never a copy taken at startup.</summary>
    public AppSettings Preferences => _preferences;

    // ── source ───────────────────────────────────────────────────────
    public bool HasSource => _sourcePath is not null;
    public string SourceName => _sourcePath is null ? "" : Path.GetFileName(_sourcePath);
    public string SourcePath => _sourcePath ?? "";
    public string TrianglesText => _sourceInfo is { } info ? Format.Count(info.TriangleCount) : "—";
    public string DimensionsText => _sourceInfo is { } info
        ? Format.Dimensions(info.SizeX, info.SizeY, info.SizeZ)
        : "—";

    // ── run state ────────────────────────────────────────────────────
    public bool IsBusy => _isBusy;
    public bool CanConvert => HasSource && !IsBusy;
    public bool CanCancel => _conversion is not null;
    public bool CanExport => HasResult && !IsBusy;
    public string StageText => string.IsNullOrEmpty(_stage) ? "READY" : _stage.ToUpperInvariant();

    /// <summary>"2:13 · 97% CPU", plus "no progress update for 1:40" when the last real progress is
    /// old. Empty when nothing is running.</summary>
    public string ActivityText => _activity.Describe(DateTime.UtcNow);
    public double Progress => _progress;
    public double ProgressShare => _progress / 100.0;
    public string ProgressText => _progress.ToString("0", CultureInfo.InvariantCulture) + "%";
    public string ErrorText => _error ?? "";
    public bool HasError => _error is not null;

    /// <summary>Honest caption for the error card: a file that never opened is not a failed conversion.</summary>
    public string ErrorTitle => _errorTitle;

    /// <summary>Where the bundle from the last failure was written, if one was captured.</summary>
    public string? DiagnosticPath => _diagnosticPath;
    public bool HasDiagnostic => _diagnosticPath is not null;
    public string DiagnosticName => _diagnosticPath is null ? "" : Path.GetFileName(_diagnosticPath);

    /// <summary>The last action's own words ("Saved part.step", "Conversion cancelled"). It takes
    /// precedence over the size summary so a silent Save or an export failure always says something.</summary>
    public string StatusRight => _feedback
        ?? (_result is not null
            ? $"{Format.Count(_result.TriangleCount)} triangles   ·   {Format.Seconds(_result.ElapsedSeconds)}"
            : HasSource ? $"{TrianglesText} triangles" : "No model loaded");

    public bool ShowGrid
    {
        get => _showGrid;
        set => Set(ref _showGrid, value);
    }

    public bool ShowAxes
    {
        get => _showAxes;
        set => Set(ref _showAxes, value);
    }

    // ── result ───────────────────────────────────────────────────────
    public bool HasResult => _result is not null;
    public bool IsValid => _result?.IsValidSolid == true;
    public string ValidityText => _result is null ? "NO RESULT" : IsValid ? "VALID SOLID" : "NOT VALID";
    public string FacesText => _result is null ? "—" : Format.Count(_result.FaceCount);
    public string SolidsText => _result is null ? "—" : Format.Count(_result.SolidCount);
    public string VolumeText => _result is null ? "—" : Format.Volume(_result.Volume);
    public string DeviationText => _result is null ? "—" : Format.Millimetres(_result.SurfaceDeviation);
    public string ToleranceText => _result is null ? "—" : Format.Millimetres(_result.FitTolerance);
    public string EdgesText => _result is null ? "—" : Format.Count(_result.Edges);
    public string TriangleCountText => _result is null ? "—" : Format.Count(_result.TriangleCount);
    public string ElapsedText => _result is null ? "—" : Format.Seconds(_result.ElapsedSeconds);
    public string KindText => _result is null ? "—" : _result.Kind == "prismatic" ? "PRISMATIC" : "FACETED FALLBACK";

    // ── surface mix ──────────────────────────────────────────────────
    public int PlaneCount => _mix.Planes;
    public int CylinderCount => _mix.Cylinders;
    public int ConeCount => _mix.Cones;
    public double PlaneShare => _mix.PlaneShare;
    public double CylinderShare => _mix.CylinderShare;
    public double ConeShare => _mix.ConeShare;
    public string PlanePercentText => Percent(_mix.PlaneShare);
    public string CylinderPercentText => Percent(_mix.CylinderShare);
    public string ConePercentText => Percent(_mix.ConeShare);

    private static string Percent(double share) =>
        (share * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    public bool HasMix => _mix.Total > 0;
    public int FacetedTriangles => _mix.FacetedTriangles;
    public bool HasFacetedFallback => _mix.FacetedTriangles > 0;
    public string FacetedText => $"{Format.Count(_mix.FacetedTriangles)} triangles fell back to faceted";

    public bool HasWarnings => Warnings.Count > 0;

    // ── commands ─────────────────────────────────────────────────────
    public async Task OpenAsync()
    {
        var path = _dialogs.PickStl();
        if (path is not null) await LoadFileAsync(path);
    }

    /// <summary>Probing welds the whole STL, which on a dense mesh takes seconds: it runs off the
    /// UI thread so the shell stays alive and the status bar can say LOADING.</summary>
    public async Task LoadFileAsync(string path)
    {
        if (!File.Exists(path))
        {
            SetError($"File not found: {path}", CouldNotOpenTitle);
            return;
        }

        int load = ++_loadGeneration;
        var previousStage = _stage;      // a failed probe must not leave the bar stuck on LOADING
        BeginBusy();
        _stage = "loading";
        _progress = 0;
        _feedback = null;
        OnAllChanged();
        RefreshCommands();

        try
        {
            var info = await Task.Run(() => _probe.Describe(path));

            // Another pick started while this one was welding: that one owns the shell now.
            if (load != _loadGeneration) return;

            _conversion?.Cancel();     // the old source is gone, so its work is wasted
            _generation++;
            _sourcePath = path;
            _sourceInfo = info;
            _result = null;
            _mix = FaceMix.Empty;
            _progress = 0;
            _stage = "";
            _stageIndex = -1;
            MarkStages(-1);
            _error = null;
            _errorTitle = ConversionFailedTitle;
            _diagnosticPath = null;
            Warnings.Clear();
            SourceLoaded?.Invoke(this, path);
            RememberRecent(path);
        }
        catch (Exception ex)
        {
            if (load != _loadGeneration) return;
            _stage = previousStage;      // a conversion, if one is running, still owns the stage line
            SetError(ex.Message, CouldNotOpenTitle);
        }
        finally
        {
            if (load == _loadGeneration) EndBusy();
        }
    }

    public async Task ConvertAsync()
    {
        if (_sourcePath is null || _isBusy) return;

        var generation = _generation;
        var cancellation = new CancellationTokenSource();
        _conversion = cancellation;

        BeginBusy();
        _progress = 0;
        _stage = "reading";
        _stageIndex = 0;
        _feedback = null;
        _diagnosticPath = null;
        MarkStages(0);
        _error = null;
        _errorTitle = ConversionFailedTitle;
        OnAllChanged();
        RefreshCommands();

        _activity.Start(DateTime.UtcNow);
        StartTicker();

        var progress = new UiProgress(p => ApplyProgress(p, generation), _ui);
        try
        {
            var result = await _converter.ConvertAsync(_sourcePath, CurrentSettings, progress, cancellation.Token);

            // A load or clear happened while this ran: the result describes a file that is no
            // longer on screen, so publishing it would mislabel the model and let Save export
            // the wrong shape. Drop it; `finally` still releases the busy flag.
            if (generation != _generation) return;

            _result = result;
            _mix = FaceMix.FromFile(result.FacesPath);
            Warnings.Clear();
            foreach (var warning in result.Warnings) Warnings.Add(warning);
            _progress = 100;
            _stage = "";
            _stageIndex = Stages.Count;
            MarkStages(Stages.Count);
            ResultReady?.Invoke(this, result);
        }
        catch (OperationCanceledException)
        {
            // Cancelled by the user (a superseding load returns earlier and keeps its own state).
            if (generation != _generation) return;
            _stage = "";
            _progress = 0;
            _stageIndex = -1;
            MarkStages(-1);
            _feedback = "Conversion cancelled";
        }
        catch (Exception ex)
        {
            if (generation != _generation) return;
            string failedStage = _stage;
            _error = ex.Message;
            _errorTitle = ConversionFailedTitle;
            _stage = "";
            MarkStages(Math.Max(_stageIndex, 0), failed: true);

            // A bundle is the difference between a bug report and a screenshot. It is written off
            // the UI thread: hashing the model is disk work, and the window must stay alive.
            if (_diagnostics is not null)
            {
                var inputs = new DiagnosticInputs(_sourcePath, _errorTitle, ex.Message, failedStage,
                    typeof(MainViewModel).Assembly.GetName().Version?.ToString() ?? "unknown",
                    DateTime.UtcNow);
                _diagnosticPath = await Task.Run(() => _diagnostics.Capture(inputs));
                OnPropertyChanged(nameof(DiagnosticPath));
                OnPropertyChanged(nameof(HasDiagnostic));
                OnPropertyChanged(nameof(DiagnosticName));
                RefreshCommands();
            }
        }
        finally
        {
            StopTicker();
            _activity.Stop();
            OnPropertyChanged(nameof(ActivityText));
            if (ReferenceEquals(_conversion, cancellation)) _conversion = null;
            cancellation.Dispose();
            EndBusy();
        }
    }

    public void Cancel() => _conversion?.Cancel();

    public void Clear()
    {
        _conversion?.Cancel();
        _loadGeneration++;      // a probe still running must not resurrect the cleared source
        _generation++;
        _sourcePath = null;
        _sourceInfo = null;
        _result = null;
        _mix = FaceMix.Empty;
        _progress = 0;
        _stage = "";
        _stageIndex = -1;
        _feedback = null;
        MarkStages(-1);
        _error = null;
        _errorTitle = ConversionFailedTitle;
        _diagnosticPath = null;
        Warnings.Clear();
        SourceCleared?.Invoke(this, EventArgs.Empty);
        OnAllChanged();
        RefreshCommands();
    }

    // ── internals ────────────────────────────────────────────────────
    private void Save(string? pipelinePath)
    {
        if (pipelinePath is null)
        {
            SetFeedback("Export unavailable: convert a model first");
            return;
        }

        var isIges = Path.GetExtension(pipelinePath).Equals(".igs", StringComparison.OrdinalIgnoreCase);
        if (!File.Exists(pipelinePath))
        {
            // The pipeline writes into the temp directory; the OS may have cleaned it since.
            SetFeedback("Export unavailable: the conversion output is no longer on disk");
            return;
        }

        var filter = isIges ? "IGES (*.igs)|*.igs" : "STEP (*.step)|*.step";
        var extension = isIges ? ".igs" : ".step";
        var stem = HasSource ? Path.GetFileNameWithoutExtension(SourceName) : "part";
        var target = _dialogs.PickSave(filter, stem + extension,
                                       initialDirectory: _preferences.DefaultOutputFolder);
        if (target is null) return;      // the user closed the dialog: no news is good news

        try
        {
            File.Copy(pipelinePath, target, overwrite: true);
            SetFeedback($"Saved {Path.GetFileName(target)}");
        }
        catch (Exception ex)
        {
            // Export problems belong next to the action that caused them (the status bar), not in
            // the source rail's card, and they are not conversion failures.
            SetFeedback($"Export failed: {ex.Message}");
        }
    }

    /// <summary>Copies the captured bundle where the user wants it; the original stays in %TEMP%.</summary>
    private void SaveDiagnosticBundle()
    {
        if (_diagnosticPath is null || !File.Exists(_diagnosticPath))
        {
            SetFeedback("Diagnostic bundle is no longer on disk");
            return;
        }

        var target = _dialogs.PickSave("Zip archive (*.zip)|*.zip", Path.GetFileName(_diagnosticPath),
                                       initialDirectory: _preferences.DefaultOutputFolder);
        if (target is null) return;

        try
        {
            File.Copy(_diagnosticPath, target, overwrite: true);
            SetFeedback($"Saved {Path.GetFileName(target)}");
        }
        catch (Exception ex)
        {
            SetFeedback($"Could not save the bundle: {ex.Message}");
        }
    }

    private void ApplyProgress(ConversionProgress progress, int generation)
    {
        if (generation != _generation) return;   // stale reporter from a superseded conversion

        // A beat proves the child is alive and busy; it says nothing about where the work is.
        if (progress.IsBeat)
        {
            _activity.Beat(progress.CpuPercent, DateTime.UtcNow);
            OnPropertyChanged(nameof(ActivityText));
            return;
        }

        _activity.Progress(DateTime.UtcNow);
        if (progress.Stage != _stage) App.Log($"stage {progress.Stage} at {DateTime.Now:HH:mm:ss}");

        _progress = Math.Clamp(progress.Fraction * 100, 0, 100);
        _stage = progress.Stage;
        int index = Array.IndexOf(StageKeys, progress.Stage);
        if (index >= 0)
        {
            _stageIndex = index;
            MarkStages(index);
        }
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(ProgressShare));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(StageText));
        OnPropertyChanged(nameof(ActivityText));
    }

    private void StartTicker()
    {
        _ticker?.Dispose();
        _ticker = new Timer(_ => Post(() => OnPropertyChanged(nameof(ActivityText))), null, 250, 250);
    }

    private void StopTicker()
    {
        _ticker?.Dispose();
        _ticker = null;
    }

    /// <summary>Inline without a UI context (tests), posted to it otherwise.</summary>
    private void Post(Action action)
    {
        if (_ui is null) action();
        else _ui.Post(_ => action(), null);
    }

    /// <summary>Everything before <paramref name="activeIndex"/> is Done, that one is Active, the rest Pending.</summary>
    private void MarkStages(int activeIndex, bool failed = false)
    {
        for (int i = 0; i < Stages.Count; i++)
        {
            Stages[i].State = i < activeIndex ? StageState.Done
                : i == activeIndex ? (failed ? StageState.Failed : StageState.Active)
                : StageState.Pending;
        }
    }

    private void SetError(string message, string? title = null)
    {
        _error = message;
        _errorTitle = title ?? ConversionFailedTitle;
        OnPropertyChanged(nameof(ErrorText));
        OnPropertyChanged(nameof(ErrorTitle));
        OnPropertyChanged(nameof(HasError));
    }

    private void SetFeedback(string message)
    {
        _feedback = message;
        OnPropertyChanged(nameof(StatusRight));
    }

    private void BeginBusy()
    {
        _busyOperations++;
        _isBusy = true;
    }

    /// <summary>Pairs with <see cref="BeginBusy"/>; the flag clears only when the last one leaves.</summary>
    private void EndBusy()
    {
        if (_busyOperations > 0) _busyOperations--;
        _isBusy = _busyOperations > 0;
        OnAllChanged();
        RefreshCommands();
    }

    private ConversionSettings CurrentSettings => new(_preferences.Repair,
                                                      _preferences.VerifyArtifacts,
                                                      ParseTolerance());

    /// <summary>Records the file the user just opened and writes the list straight through: the
    /// recent list is the one setting whose whole point is surviving a crash, not a clean close.</summary>
    private void RememberRecent(string path)
    {
        // The property of the same name shadows the static class inside this type.
        var updated = Services.RecentFiles.Touch(RecentFiles, path);
        RecentFiles.Clear();
        foreach (var entry in updated) RecentFiles.Add(entry);

        _preferences = _preferences with { Recent = updated };
        _settings?.Save(_preferences);
    }

    /// <summary>A missing file is a fact about the disk, not an error: the entry is dropped with a
    /// sentence in the status bar and nothing else happens.</summary>
    public async Task OpenRecentAsync(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;

        if (!File.Exists(path))
        {
            var kept = Services.RecentFiles.Without(RecentFiles, path);
            RecentFiles.Clear();
            foreach (var entry in kept) RecentFiles.Add(entry);
            _preferences = _preferences with { Recent = kept };
            _settings?.Save(_preferences);
            SetFeedback($"Removed {Path.GetFileName(path)}: the file is no longer on disk");
            return;
        }

        await LoadFileAsync(path);
    }

    /// <summary>Both separators, because the machine is Turkish and the datasheets are not.</summary>
    private double? ParseTolerance() =>
        Services.ToleranceInput.TryParse(_toleranceInput, out double value) ? value : null;

    private void RefreshCommands()
    {
        OpenCommand.RaiseCanExecuteChanged();
        ConvertCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        SaveStepCommand.RaiseCanExecuteChanged();
        SaveIgesCommand.RaiseCanExecuteChanged();
        ClearCommand.RaiseCanExecuteChanged();
        SaveDiagnosticBundleCommand.RaiseCanExecuteChanged();
        ReRunCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Runs inline when there is no UI context (tests), posts to the UI otherwise.</summary>
    private sealed class UiProgress : IProgress<ConversionProgress>
    {
        private readonly Action<ConversionProgress> _apply;
        private readonly SynchronizationContext? _ui;

        public UiProgress(Action<ConversionProgress> apply, SynchronizationContext? ui)
        {
            _apply = apply;
            _ui = ui;
        }

        public void Report(ConversionProgress value)
        {
            if (_ui is null) _apply(value);
            else _ui.Post(_ => _apply(value), null);
        }
    }
}
