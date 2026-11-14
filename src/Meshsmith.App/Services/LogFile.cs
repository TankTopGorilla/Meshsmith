using System.IO;

namespace Meshsmith.App.Services;

/// <summary>The app log as two files instead of one that vanishes when it grows too big.
///
/// <para>The old behaviour deleted the live log at 2 MB, and only on startup: a long session grew
/// without bound, and the previous session's evidence was destroyed exactly when a bug report
/// needed it. This rotates instead - &lt;name&gt; is the live log, &lt;name&gt;.1 is the session before
/// it - and the check sits on the write, so a session that runs for hours still stops at the
/// limit. Exactly two files exist at any time.</para>
///
/// <para>Nothing here throws: a missing log line is never worth failing a conversion over, and two
/// instances sharing one directory must keep appending rather than crash.</para></summary>
public sealed class LogFile
{
    public const long DefaultLimitBytes = 2L * 1024 * 1024;

    private readonly string _directory;
    private readonly string _name;
    private readonly long _limitBytes;

    public LogFile(string directory, string name = "meshsmith-app.log",
                   long limitBytes = DefaultLimitBytes)
    {
        _directory = directory;
        _name = name;
        _limitBytes = limitBytes;
    }

    /// <summary>The live log.</summary>
    public string Path => System.IO.Path.Combine(_directory, _name);

    /// <summary>The previous session's log. May not exist.</summary>
    public string PreviousPath => Path + ".1";

    /// <summary>Begins a session: rotate last session's log if it is already over the limit, then
    /// write the banner that makes a restart visible.</summary>
    public void Start()
    {
        try
        {
            if (File.Exists(Path) && new FileInfo(Path).Length > _limitBytes) Rotate();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Another instance holds it; keep going with whatever is there.
        }

        Append($"=== start {DateTime.Now:yyyy-MM-dd HH:mm:ss} (pid {Environment.ProcessId}) ===");
    }

    /// <summary>Appends one line, then rotates if that pushed the file over the limit: the check
    /// belongs on the write, not only at startup, or a long session grows without bound.</summary>
    public void Append(string message)
    {
        try
        {
            File.AppendAllText(Path, $"{DateTime.Now:HH:mm:ss.fff} {message}\n");
            if (new FileInfo(Path).Length > _limitBytes) Rotate();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A missing log line is never worth failing a conversion over.
        }
    }

    /// <summary>&lt;name&gt; becomes &lt;name&gt;.1. Whatever was already there is two sessions old and
    /// is the only thing this ever deletes.</summary>
    public void Rotate()
    {
        try
        {
            if (!File.Exists(Path)) return;
            if (File.Exists(PreviousPath)) File.Delete(PreviousPath);
            File.Move(Path, PreviousPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The other instance rotated first; the loser keeps appending to whatever is there.
        }
    }
}
