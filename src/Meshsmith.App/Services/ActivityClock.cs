namespace Meshsmith.App.Services;

/// <summary>
/// Turns the handful of progress messages a conversion emits into a line that moves even when the
/// percentage does not: elapsed time always advances, a worker beat reports how hard the child is
/// actually working, and a long silence is named instead of hidden behind a frozen bar.
/// <para>Pure: every method takes "now", so the rules are testable without waiting on a clock.</para>
/// </summary>
public sealed class ActivityClock
{
    private static readonly TimeSpan QuietAfter = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SilentAfter = TimeSpan.FromSeconds(6);

    private DateTime? _started;
    private DateTime _lastProgress;
    private DateTime _lastBeat;
    private double _cpuPercent;

    public bool Running => _started.HasValue;

    public void Start(DateTime now)
    {
        _started = now;
        _lastProgress = now;
        _lastBeat = now;
        _cpuPercent = 0;
    }

    /// <summary>A real progress message: the pipeline moved to another stage or fraction.</summary>
    public void Progress(DateTime now)
    {
        if (Running) _lastProgress = now;
    }

    /// <summary>A heartbeat from the worker process, with its own CPU share since the last beat.</summary>
    public void Beat(double cpuPercent, DateTime now)
    {
        if (!Running) return;
        _lastBeat = now;
        _cpuPercent = cpuPercent;
    }

    public void Stop() => _started = null;

    /// <summary>
    /// "" before a conversion; otherwise "2:13 · 97% CPU", with " · no progress update for 1:40"
    /// appended once the last real progress message is old, or "worker silent for 0:08" when the
    /// beats stop — which is the only case that actually means stuck.
    /// </summary>
    public string Describe(DateTime now, TimeSpan? quietAfter = null, TimeSpan? silentAfter = null)
    {
        if (_started is not { } started) return "";

        TimeSpan silence = now - _lastBeat;
        if (silence > (silentAfter ?? SilentAfter))
            return $"worker silent for {Duration(silence)}";

        var parts = new List<string> { Duration(now - started) };
        if (_cpuPercent > 0) parts.Add($"{_cpuPercent:0}% CPU");

        TimeSpan quiet = now - _lastProgress;
        if (quiet > (quietAfter ?? QuietAfter))
            parts.Add($"no progress update for {Duration(quiet)}");

        return string.Join("  ·  ", parts);
    }

    private static string Duration(TimeSpan span) =>
        span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes}:{span.Seconds:00}";
}
