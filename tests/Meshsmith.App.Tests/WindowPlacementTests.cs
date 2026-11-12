using System.Windows;
using Meshsmith.App.Services;

namespace Meshsmith.App.Tests;

/// <summary>Pure arithmetic on two rectangles: a monitor can be unplugged between sessions, and a
/// window restored onto a screen that is no longer there is invisible with no way back.</summary>
public class WindowPlacementTests
{
    private static readonly Rect Work = new(0, 0, 1920, 1080);

    [Fact]
    public void A_window_inside_the_work_area_is_left_alone()
    {
        var clamped = WindowPlacement.Clamp(new Rect(100, 50, 1200, 800), Work);

        Assert.Equal(new Rect(100, 50, 1200, 800), clamped);
    }

    [Fact]
    public void A_window_larger_than_the_work_area_is_shrunk_to_fit()
    {
        var clamped = WindowPlacement.Clamp(new Rect(100, 100, 2400, 1300), Work);

        Assert.Equal(new Rect(0, 0, 1920, 1080), clamped);
    }

    [Fact]
    public void A_window_off_every_screen_is_centred_on_the_work_area()
    {
        var clamped = WindowPlacement.Clamp(new Rect(5000, 4000, 1000, 800), Work);

        Assert.Equal(new Rect(460, 140, 1000, 800), clamped);
    }

    [Fact]
    public void A_window_overlapping_the_edge_is_moved_inside()
    {
        var clamped = WindowPlacement.Clamp(new Rect(-200, 300, 1000, 800), Work);

        Assert.Equal(new Rect(0, 280, 1000, 800), clamped);
    }

    [Fact]
    public void A_maximized_window_is_captured_with_its_restore_bounds()
    {
        var captured = WindowPlacement.Capture(120.4, 80.6, 1439.7, 900.2, maximized: true);

        Assert.Equal(120, captured.X);
        Assert.Equal(81, captured.Y);
        Assert.Equal(1440, captured.W);
        Assert.Equal(900, captured.H);
        Assert.True(captured.Maximized);

        // A normal window captures the same numbers and the flag off.
        Assert.False(WindowPlacement.Capture(10, 20, 800, 600, maximized: false).Maximized);
    }
}
