using System.Globalization;
using System.Text.RegularExpressions;
using Meshsmith.App.Services;
using Xunit;

namespace Meshsmith.App.Tests;

/// <summary>The status bar's percentage is an effort fraction, not a time fraction, so the number
/// next to it has to be computed from how long stages actually take. These tests replay measured
/// runs - including the Part 1 run whose `sewing 0.75` arrives 0.8 % of the way in, which is the
/// case a fraction-derived estimate gets wrong by 3x.</summary>
public class EtaEstimatorTests
{
    private static readonly DateTime T0 = new(2026, 11, 14, 23, 0, 0, DateTimeKind.Utc);
    private static DateTime At(double seconds) => T0.AddSeconds(seconds);

    /// <summary>Reads the number the user sees, so the assertion is on the rendered text.</summary>
    private static double Seconds(string text)
    {
        var match = Regex.Match(text, @"^~([0-9]+) (s|min)( |$)");
        Assert.True(match.Success, $"not an estimate: '{text}'");
        double value = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        return match.Groups[2].Value == "min" ? value * 60 : value;
    }

    [Fact]
    public void Nothing_is_shown_before_three_percent()
    {
        var eta = new EtaEstimator();

        Assert.Equal("", eta.Text(At(0)));           // never started

        eta.Start(At(0), 2_784);
        eta.Report("reading", 0.02, At(0));
        Assert.Equal("", eta.Text(At(0.1)));         // under three percent

        eta.Report("reading", 0.05, At(0.2));
        Assert.Equal("", eta.Text(At(0.2)));         // no stage has completed: no sample yet

        eta.Report("recognising", 0.20, At(0.5));    // `reading` is now measured
        Assert.NotEqual("", eta.Text(At(0.5)));
    }

    [Fact]
    public void The_estimate_never_increases_within_a_stage()
    {
        var eta = new EtaEstimator();
        eta.Start(At(0), 2_784);
        eta.Report("reading", 0.05, At(0));
        eta.Report("recognising", 0.20, At(0.02));   // reading measured 0.02 s
        eta.Report("building", 0.55, At(0.20));      // recognising measured 0.18 s

        double first = Seconds(eta.Text(At(0.21)));
        double later = Seconds(eta.Text(At(30)));
        double backwards = Seconds(eta.Text(At(0.22)));   // the clock is allowed to step back

        Assert.True(later <= first, $"{later} s rose above {first} s inside one stage");
        Assert.True(backwards <= later, $"{backwards} s rose above {later} s inside one stage");
    }

    [Fact]
    public void The_estimate_smooths_over_the_last_three_stage_samples()
    {
        var eta = new EtaEstimator();
        eta.Start(At(0), 2_784);                     // the P1 run: priors are its own timings

        eta.Report("reading", 0.05, At(0));
        eta.Report("recognising", 0.20, At(0.02));        // sample 1: 0.02 / 0.02  = 1.0
        eta.Report("building", 0.55, At(0.12));           // sample 2: 0.10 / 0.05  = 2.0
        eta.Report("sewing", 0.75, At(0.92));             // sample 3: 0.80 / 0.40  = 2.0
        eta.Report("validating", 0.90, At(19.52));        // sample 4: 18.60 / 1.86 = 10.0

        // Last three only: (2.0 + 2.0 + 10.0) / 3 = 4.667, so `validating` is 2.72 x 4.667 = 12.7 s.
        // All four would say 10.2 s ("~10 s"); the newest alone would say 27.2 s ("~27 s").
        Assert.Equal(13, Seconds(eta.Text(At(19.52))));
    }

    [Fact]
    public void The_estimate_matches_the_measured_part_one_timings()
    {
        // Part 1: 12,767 triangles, 83.23 s total, and `sewing 0.75` arrives after
        // 0.03 + 0.17 + 0.48 = 0.68 s - 0.8 % of the run. The truth is 82.5 s left.
        var eta = new EtaEstimator();
        eta.Start(At(0), 12_767);
        eta.Report("reading", 0.05, At(0));
        eta.Report("recognising", 0.20, At(0.03));
        eta.Report("building", 0.55, At(0.20));
        eta.Report("sewing", 0.75, At(0.68));

        double remaining = Seconds(eta.Text(At(0.68)));

        Assert.InRange(remaining, 82.5 * 0.75, 82.5 * 1.25);
    }

    [Fact]
    public void A_repeated_sewing_stage_is_a_labelled_rebaseline()
    {
        var eta = new EtaEstimator();
        eta.Start(At(0), 12_767);
        eta.Report("reading", 0.05, At(0));
        eta.Report("recognising", 0.20, At(0.03));
        eta.Report("building", 0.55, At(0.20));
        eta.Report("sewing", 0.75, At(0.68));

        double beforeRebase = Seconds(eta.Text(At(70.68)));   // 70 s into sewing

        // The faceted fallback re-emits `sewing`, which is the only way the stage can report twice.
        eta.Report("sewing", 0.85, At(70.68));
        Assert.True(eta.IsRebaselined);

        var text = eta.Text(At(70.68));
        Assert.Contains("re-based", text);
        Assert.True(Seconds(text) > beforeRebase, $"{Seconds(text)} s did not re-base above {beforeRebase} s");

        Assert.False(eta.IsRebaselined);                      // true for exactly one call
        Assert.DoesNotContain("re-based", eta.Text(At(70.68)));
    }
}
