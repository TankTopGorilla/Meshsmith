using Meshsmith.Core.Export;

namespace Meshsmith.Core.Pipeline;

/// <summary>
/// Decides whether a conversion counts as successful. The written file is the contract: when it was
/// checked, it decides - in both directions. The in-memory body only speaks for files that were not
/// checked, because a body that fails an in-memory check can still write a file that re-imports clean.
/// Measured on the user's own parts: three assembly exports failed the in-memory solid check while
/// their written STEP verified with matching faces and volume.
/// </summary>
public static class SolidVerdict
{
    public static (bool Valid, string? Note) Decide(bool bodyValid, ArtifactCheck artifact)
    {
        if (artifact.Status == ArtifactStatus.Skipped)
            return (bodyValid, null);

        bool fileValid = artifact.Status == ArtifactStatus.Verified;

        if (fileValid) return (true, bodyValid ? null :
            "the in-memory body failed the solid check; the exported file reads back valid " +
            "with matching faces and volume");

        // A mismatch is already reported by the caller; a valid body whose file does not match is
        // still a failed conversion, because the file is what leaves the machine.
        return (false, null);
    }
}
