namespace Meshsmith.App.Services;

/// <summary>What the app remembers between sessions, as one small JSON file.
///
/// <para>Every field carries an <c>init</c> default on purpose. A positional record would give a
/// missing key the CLR default, so a sparse or downgraded file would arrive with
/// <c>Repair=false</c> and silently switch repair and verification off. With init defaults a missing
/// key means "as it always was".</para></summary>
public sealed record AppSettings
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;
    public bool Repair { get; init; } = true;
    public bool VerifyArtifacts { get; init; } = true;
    public string? DefaultOutputFolder { get; init; }
    public List<string> Recent { get; init; } = new();
    public WindowPlacement? Window { get; init; }

    public static AppSettings Default => new();
}

/// <summary>Where the window was, in device-independent pixels. Named Placement rather than
/// WindowState so it cannot be confused with <see cref="System.Windows.WindowState"/>.
/// <para>Partial so the arithmetic lives in <c>WindowPlacement.cs</c> while the shape stays with the
/// settings it is serialised with.</para></summary>
public sealed partial record WindowPlacement(int X, int Y, int W, int H, bool Maximized);
