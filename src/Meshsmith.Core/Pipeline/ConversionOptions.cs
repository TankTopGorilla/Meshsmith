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

    /// <summary>Dense meshes are decimated before anything is fitted to them. The classifier's fits are
    /// per-triangle and its region growing is quadratic-ish in the count, so a part this large never
    /// finishes at all (the 386,266-triangle knob times out at 900 s). Measured: 386,266 -> 60,000
    /// triangles in 1.08 s through geometry3Sharp, still closed, one body; the classifier still fits
    /// a 40 k mesh at 0.9989x tol, so 60 k is strictly safer. 0 disables the branch.</summary>
    public int DecimateAbove { get; init; } = 200_000;

    /// <summary>What a mesh above <see cref="DecimateAbove"/> is reduced to. Lower it if the measured
    /// deviation ever exceeds 2x tol - that is the detail erasure the report prints.</summary>
    public int DecimateTarget { get; init; } = 60_000;

    /// <summary>Decimate whatever comes in, threshold or not. The CLI seam (<c>--decimate</c>) and the
    /// only way to exercise the path on a small part.</summary>
    public bool ForceDecimate { get; init; }

    /// <summary>The user's claim that a file's numbers are already millimetres. <c>null</c> - the
    /// default - leaves the decision to the format: glTF is metres by spec (x1000), FBX declares
    /// nothing and is taken as millimetres (x1). <c>false</c> says the numbers are metres, which is
    /// the only override a unit-less format needs.</summary>
    public bool? TreatNumbersAsMillimetres { get; init; }

    /// <summary>Byte-level read progress, 0..1. Separate from <see cref="Progress"/>, which is the
    /// stage percentage: a 386 k STL and a 12-triangle one differ hugely here and not at all there.</summary>
    public IProgress<double>? ByteProgress { get; init; }

    /// <summary>The readers a conversion may use. Null means the built-in registry order; a list is the
    /// injection seam that proves the pipeline really goes through <c>MeshReaderRegistry</c>.</summary>
    public IReadOnlyList<Import.IMeshReader>? Readers { get; init; }

    /// <summary>Which segmenter turns the mesh into regions.</summary>
    public SegmenterKind Segmenter { get; init; } = SegmenterKind.Flood;

    public Action<string, double>? Progress { get; init; }
}

/// <summary>
/// The two region producers. Both return the same <c>List&lt;ClassifiedRegion&gt;</c> and go through
/// the same merge-and-split tail, so everything downstream (FaceBuilder, the sew ladder, the report)
/// is indifferent to which one ran.
/// </summary>
public enum SegmenterKind
{
    /// <summary>
    /// Today's behaviour and the default: dihedral flood fill, then one fit per grown region. Every
    /// part must stay byte-identical until the hypothesis engine is proven on the knob, so this is
    /// the rollback lever as well as the default - with no flag the pipeline executes the identical
    /// <c>PrimitiveClassifier.Classify</c> call it executed before the flag existed.
    /// </summary>
    Flood,

    /// <summary>
    /// The hypothesis engine: flood first, then sequential RANSAC over the residual flood refused,
    /// then a per-component rescue. It only ever ADDS regions in the residual, so it cannot lose a
    /// region flood found.
    /// </summary>
    Ransac
}
