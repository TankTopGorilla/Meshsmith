using System.Globalization;

namespace Meshsmith.App.Services;

/// <summary>How much longer a conversion will take, measured rather than guessed.
///
/// <para>The status bar's percentage is an effort fraction, not a time fraction, and the gap is
/// not small: `sewing 0.75` arrives after 0.03 + 0.17 + 0.48 = 0.68 s of an 83.23 s Part 1 run
/// (0.8 %), so the naive "remaining = (1-f)/f x elapsed" answers 27.7 s where the truth is 82.5 s
/// - 3.0x short. On the dense knob it says 8.5 s against 1,134 s, 133x short. So `fraction` is
/// used for exactly two things here: did the stage change, and - for `reading` only, which after
/// the byte-progress work is the one stage whose fraction is real - how far into that stage we are.
/// Everything else counts down on durations.</para>
///
/// <para>The stage table is a prior from three measured runs; every stage that completes replaces
/// its own row for the rest of the run, and the last three measured ratios are averaged so one
/// odd stage cannot swing the answer. Pure arithmetic: no clock, no I/O, every call takes `now`,
/// so a test can replay a measured run exactly.</para></summary>
public sealed class EtaEstimator
{
    /// <summary>The seven stages the pipeline reports, in execution order.</summary>
    private static readonly string[] Stages =
    {
        "reading", "recognising", "building", "sewing", "writing", "verifying", "validating"
    };

    /// <summary>Seconds per stage, measured on this machine on 2026-11-14. The three columns are
    /// the three runs that exist, and <see cref="AnchorTriangles"/> holds the triangle count each
    /// was measured at, so "scaled by triangles" reproduces every measured row exactly at its own
    /// size. The dense column is a single run (UNVERIFIED as a general prior) - which is precisely
    /// why the first completed stage re-fits the whole run instead of trusting it.</summary>
    private static readonly double[][] PriorTable =
    {
        // reading  recognising  building  sewing   writing  verifying  validating
        new[] { 0.02,   0.05,       0.40,     1.86,    0.41,    1.06,      2.72 },   // P1, 2,784 tris
        new[] { 0.03,   0.17,       0.48,     39.30,   3.54,    23.36,     16.36 },  // Part 1, 12,767 tris
        new[] { 0.96,   20.90,      3.66,     938.33,  22.89,   128.36,    44.73 }   // knob, 386,266 tris
    };

    private static readonly long[] AnchorTriangles = { 2_784, 12_767, 386_266 };
    private static readonly long[] BandTop = { 3_000, 15_000, long.MaxValue };

    /// <summary>Ratios of measured to predicted duration for the last three completed stages.</summary>
    private readonly Queue<double> _ratios = new();
    private readonly HashSet<string> _done = new(StringComparer.Ordinal);

    private bool _started;
    private long _triangles;
    private string? _stage;
    private DateTime _stageStarted;
    private double _fraction;
    private double _lastShown = double.MaxValue;
    private bool _completedAny;
    private bool _rebaselineLabel;
    private int _sewingReports;

    /// <summary>True for the one <see cref="Text"/> call that renders a re-baseline's reason.</summary>
    public bool IsRebaselined => _rebaselineLabel;

    /// <summary>Begins a run. <paramref name="triangles"/> is the mesh size the priors scale with;
    /// zero means "not known", which falls back to the smallest measured run.</summary>
    public void Start(DateTime now, long triangles)
    {
        _ratios.Clear();
        _done.Clear();
        _started = true;
        _triangles = Math.Max(0, triangles);
        _stage = null;
        _stageStarted = now;
        _fraction = 0;
        _lastShown = double.MaxValue;
        _completedAny = false;
        _rebaselineLabel = false;
        _sewingReports = 0;
    }

    /// <summary>One progress report, verbatim from the pipeline.</summary>
    public void Report(string stage, double fraction, DateTime now)
    {
        if (!_started || string.IsNullOrWhiteSpace(stage)) return;

        var key = stage.Trim().ToLowerInvariant();

        // The faceted fallback is the only way `sewing` can report twice - ConversionPipeline emits
        // `sewing 0.85` on the per-body rebuild and again on the faceted path. The stage restarted,
        // so the countdown is allowed to grow once and says why. `reading` also emits twice
        // (0.05 / 0.15); that is the decimation sub-step, not a restart.
        if (key == "sewing")
        {
            _sewingReports++;
            if (_sewingReports > 1)
            {
                _stage = key;
                _stageStarted = now;
                _fraction = fraction;
                _lastShown = double.MaxValue;
                _rebaselineLabel = true;
                return;
            }
        }
        else
        {
            _sewingReports = 0;
        }

        if (key != _stage)
        {
            if (_stage is not null) Complete(_stage, now);

            // The pipeline is strictly sequential and never announces two of its stages: the
            // export work (`writing`, `verifying`) happens between `sewing` and `validating` and
            // only the timings line records it. Entering a stage therefore proves every earlier
            // one is done - otherwise the estimate would still be charging for the export after
            // `validating 0.90` has arrived.
            int index = Array.IndexOf(Stages, key);
            for (int i = 0; i < index; i++) _done.Add(Stages[i]);

            _stage = key;
            _stageStarted = now;
            _fraction = fraction;
            _lastShown = double.MaxValue;   // a new stage may honestly say something new
            return;
        }

        _fraction = fraction;
    }

    /// <summary>The line for the status bar: "" until there is evidence, then "~12 s" / "~2 min".</summary>
    public string Text(DateTime now)
    {
        if (!_started || _stage is null || !_completedAny) return "";
        if (_fraction <= 0.03) return "";    // 3 % of the work is not evidence of a duration

        double candidate = Math.Max(0, Remaining(now));
        _lastShown = Math.Min(candidate, _lastShown);   // it may fall, never rise, inside a stage

        var text = Format(_lastShown);
        if (_rebaselineLabel)
        {
            _rebaselineLabel = false;
            text += " (re-based: faceted fallback)";
        }
        return text;
    }

    /// <summary>Seconds left: what the current stage still owes plus every stage after it.</summary>
    private double Remaining(DateTime now)
    {
        double factor = _ratios.Count == 0 ? 1.0 : _ratios.Average();
        double spent = Math.Max(0, (now - _stageStarted).TotalSeconds);
        double total = 0;

        foreach (var stage in Stages)
        {
            if (_done.Contains(stage)) continue;

            double predicted = Prior(stage) * factor;
            if (stage == _stage)
            {
                // `reading` reports real bytes, so its fraction is the honest position in that
                // stage. Every other fraction is an effort guess that can sit unmoved for minutes
                // (writing + verifying is 32 % of Part 1 and 13 % of the knob with a frozen bar),
                // so those stages count down on the clock instead.
                predicted = stage == "reading" && _fraction > 0.03
                    ? predicted * (1 - Math.Clamp(_fraction, 0, 1))
                    : Math.Max(0, predicted - spent);
            }

            total += Math.Max(0, predicted);
        }

        return total;
    }

    /// <summary>A stage ended: its measured duration replaces the prior for the rest of the run.</summary>
    private void Complete(string stage, DateTime now)
    {
        _done.Add(stage);
        _completedAny = true;

        double prior = Prior(stage);
        if (prior <= 1e-9) return;   // an unknown stage teaches nothing

        _ratios.Enqueue(Math.Max(0, (now - _stageStarted).TotalSeconds) / prior);
        while (_ratios.Count > 3) _ratios.Dequeue();
    }

    /// <summary>The prior for one stage at this mesh's size.</summary>
    private double Prior(string stage)
    {
        int index = Array.IndexOf(Stages, stage);
        if (index < 0) return 0;

        int band = 0;
        while (band < BandTop.Length - 1 && _triangles > BandTop[band]) band++;

        double scale = _triangles is > 0 ? (double)_triangles / AnchorTriangles[band] : 1.0;
        return PriorTable[band][index] * scale;
    }

    /// <summary>Seconds are precise to a second; minutes are not precise at all.</summary>
    private static string Format(double seconds)
    {
        if (seconds < 100)
        {
            return "~" + Math.Round(seconds, MidpointRounding.AwayFromZero)
                              .ToString("0", CultureInfo.InvariantCulture) + " s";
        }

        double minutes = Math.Max(1, Math.Round(seconds / 60.0, MidpointRounding.AwayFromZero));
        return "~" + minutes.ToString("0", CultureInfo.InvariantCulture) + " min";
    }
}
