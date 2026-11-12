using Meshsmith.App.Services;
using Xunit;

namespace Meshsmith.App.Tests;

public class ActivityClockTests
{
    private static readonly DateTime T0 = new(2026, 11, 12, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Before_a_conversion_the_activity_line_is_empty()
    {
        Assert.Equal("", new ActivityClock().Describe(T0));
    }

    [Fact]
    public void After_start_it_shows_elapsed_time()
    {
        var clock = new ActivityClock();
        clock.Start(T0);

        Assert.Equal("0:05", clock.Describe(T0.AddSeconds(5)));
    }

    [Fact]
    public void A_beat_reports_how_hard_the_worker_is_working()
    {
        var clock = new ActivityClock();
        clock.Start(T0);
        clock.Beat(97, T0.AddSeconds(4));

        Assert.Equal("0:05  ·  97% CPU", clock.Describe(T0.AddSeconds(5)));
    }

    [Fact]
    public void A_long_gap_without_progress_is_named()
    {
        var clock = new ActivityClock();
        clock.Start(T0);
        clock.Beat(96, T0.AddSeconds(43));      // beats keep arriving; only progress is stuck

        string line = clock.Describe(T0.AddSeconds(45));

        Assert.Contains("0:45", line);
        Assert.Contains("no progress update for 0:45", line);
    }

    [Fact]
    public void A_progress_message_resets_the_quiet_timer()
    {
        var clock = new ActivityClock();
        clock.Start(T0);
        clock.Beat(96, T0.AddSeconds(44));
        clock.Progress(T0.AddSeconds(40));

        string line = clock.Describe(T0.AddSeconds(45));

        Assert.DoesNotContain("no progress", line);
    }

    [Fact]
    public void A_silent_worker_is_named_instead_of_the_clock()
    {
        var clock = new ActivityClock();
        clock.Start(T0);
        clock.Beat(90, T0.AddSeconds(2));

        Assert.Equal("worker silent for 0:14", clock.Describe(T0.AddSeconds(16)));
    }

    [Fact]
    public void Stopping_clears_the_line()
    {
        var clock = new ActivityClock();
        clock.Start(T0);
        clock.Stop();

        Assert.Equal("", clock.Describe(T0.AddSeconds(30)));
    }

    [Fact]
    public void Beats_after_stop_are_ignored()
    {
        var clock = new ActivityClock();
        clock.Start(T0);
        clock.Stop();
        clock.Beat(50, T0.AddSeconds(1));

        Assert.Equal("", clock.Describe(T0.AddSeconds(20)));
    }
}
