namespace Meshsmith.Core.Export;

/// <summary>What happened when the written file was read back.</summary>
public enum ArtifactStatus
{
    /// <summary>The file was read back and matches the body.</summary>
    Verified,

    /// <summary>The file was read back and does not match the body.</summary>
    Mismatch,

    /// <summary>The check was not run (switched off, or the body is above the face limit).</summary>
    Skipped
}

/// <summary>
/// What the file on disk actually contains, read back and measured. Fidelity of the file and
/// validity of the body are separate facts: a faithful export of an open shell is a good file and a
/// bad solid, and both are reported here rather than conflated.
/// </summary>
public sealed record ArtifactCheck(
    ArtifactStatus Status,
    int Faces,
    int Solids,
    double Volume,
    double VolumeDeltaPercent,
    bool IsValid,
    string Detail)
{
    /// <summary>True only when the file was read back and matched.</summary>
    public bool Ok => Status == ArtifactStatus.Verified;

    public static ArtifactCheck Skipped(string why) =>
        new(ArtifactStatus.Skipped, 0, 0, 0, 0, false, why);
}
