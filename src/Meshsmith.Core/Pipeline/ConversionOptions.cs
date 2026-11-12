namespace Meshsmith.Core.Pipeline;

public sealed class ConversionOptions
{
    public double? ToleranceOverride { get; init; }

    /// <summary>Diagnostics: keep intermediate files in WorkingDirectory. The caller owns
    /// cleanup either way - the pipeline never deletes its working directory.</summary>
    public bool KeepTemporaryFiles { get; init; }

    public string? WorkingDirectory { get; init; }

    /// <summary>Read the written file back and compare it with the body. On by default: an export
    /// nobody checked is a claim, not a result.</summary>
    public bool VerifyArtifacts { get; init; } = true;

    /// <summary>Reading a STEP back costs roughly 10 ms per face, so the check is skipped above
    /// this size: on a 12,767-face body it added two minutes to a 30 s conversion. Raise it with
    /// <c>--verify</c> when the file matters more than the wait.</summary>
    public int VerifyFaceLimit { get; init; } = 4000;

    /// <summary>Drop degenerates and duplicates, fix winding and close small holes before fitting.
    /// On by default; the report always says what was changed.</summary>
    public bool RepairMesh { get; init; } = true;

    /// <summary>Holes with more boundary edges than this are left open and reported.</summary>
    public int MaxHoleEdges { get; init; } = 64;
    public Action<string, double>? Progress { get; init; }
}
