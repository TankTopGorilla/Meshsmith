using System.IO;
using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meshsmith.App.Services;

/// <summary>One small JSON file beside the user profile, read once at startup and written on the
/// few events that change it.
///
/// <para>Persistence is a convenience, never a reason the window cannot open: every failure below
/// ends in defaults plus one log line. The write is a temporary file renamed over the target, so a
/// crash leaves either the old file or the new one, never half of either.</para></summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _path;
    private readonly Action<string>? _log;

    /// <summary>A file written by a newer build carries settings this one does not understand;
    /// rewriting it would discard them, so the session reads defaults and stays read-only.</summary>
    private bool _readOnly;

    /// <param name="path">Where the file lives. Tests point this at a scratch folder.</param>
    /// <param name="log">One-line sink for the diagnostic log; never a stack trace.</param>
    public SettingsStore(string path, Action<string>? log = null)
    {
        _path = path;
        _log = log;
    }

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Meshsmith", "settings.json");

    public AppSettings Load()
    {
        if (!File.Exists(_path))
        {
            Say("settings: no file yet, using defaults");
            return AppSettings.Default;
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), Json);
            if (settings is null)
            {
                Say("settings: the file held no object, using defaults");
                return AppSettings.Default;
            }

            if (settings.Version != AppSettings.CurrentVersion)
            {
                _readOnly = true;
                Say($"settings: version {settings.Version} is not {AppSettings.CurrentVersion}, " +
                    "using defaults and not saving");
                return AppSettings.Default;
            }

            // A window with no size is what a hand-edited or half-written block looks like; a 0x0
            // window would come back somewhere the user cannot see, so it counts as no placement.
            return settings.Window is { } window && (window.W <= 0 || window.H <= 0)
                ? settings with { Window = null }
                : settings;
        }
        catch (JsonException ex)
        {
            Say($"settings: {ex.GetType().Name}: {FirstLine(ex.Message)}");
            return AppSettings.Default;
        }
        catch (Exception ex) when (IsFileProblem(ex))
        {
            Say($"settings: {ex.GetType().Name}: {FirstLine(ex.Message)}");
            return AppSettings.Default;
        }
    }

    /// <summary>True when the file on disk now describes <paramref name="settings"/>. A failure is
    /// logged and answered with <c>false</c>: there is no dialog to show and nothing worth retrying
    /// inside one session.</summary>
    public bool Save(AppSettings settings)
    {
        if (_readOnly)
        {
            Say("settings: not saving over a file from a newer version");
            return false;
        }

        var temporary = _path + ".tmp";
        try
        {
            var folder = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Json));
            File.Move(temporary, _path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (IsFileProblem(ex))
        {
            Say($"settings: could not be saved: {ex.GetType().Name}: {FirstLine(ex.Message)}");
            TryDelete(temporary);
            return false;
        }
    }

    private void Say(string message) => _log?.Invoke(message);

    private static bool IsFileProblem(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or NotSupportedException or SecurityException;

    /// <summary>Log lines stay one line; a JSON reader's message can span several.</summary>
    private static string FirstLine(string message)
    {
        int end = message.IndexOfAny(new[] { '\r', '\n' });
        return end < 0 ? message : message[..end];
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (IsFileProblem(ex))
        {
            // A leftover temp file is noise, not a failure worth surfacing.
        }
    }
}
