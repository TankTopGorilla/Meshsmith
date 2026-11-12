using Occt;

namespace Meshsmith.Core.Export;

/// <summary>
/// Reads an export back from disk and compares it with what the pipeline believes it wrote.
/// A mismatch means the writer lost something between the body and the file - which is exactly the
/// failure a CAD importer would report later, with less information.
/// </summary>
public static class ArtifactVerifier
{
    /// <summary>STEP and IGES carry B-rep exactly, so anything above this is a writer bug.</summary>
    private const double VolumeTolerancePercent = 0.5;

    public static ArtifactCheck Verify(string path, int expectedFaces, int expectedSolids, double expectedVolume)
    {
        TopoDS_Shape shape;
        try
        {
            if (!TryRead(path, out shape))
                return new ArtifactCheck(ArtifactStatus.Mismatch, 0, 0, 0, 0, false,
                    "the file contains no transferable shape");
        }
        catch (Exception ex)
        {
            return new ArtifactCheck(ArtifactStatus.Mismatch, 0, 0, 0, 0, false,
                $"could not read the file back: {ex.Message}");
        }

        int faces = Count(shape, TopAbs_ShapeEnum.TopAbs_FACE);
        int solids = Count(shape, TopAbs_ShapeEnum.TopAbs_SOLID);
        BRepGProp.VolumeProperties(shape, out GProp_GProps props);
        double volume = props.Mass;
        bool valid = new BRepCheck_Analyzer(shape).IsValid();

        double delta = expectedVolume > 1e-9
            ? Math.Abs(volume - expectedVolume) / expectedVolume * 100
            : Math.Abs(volume) > 1e-9 ? 100 : 0;

        var problems = new List<string>();
        if (faces != expectedFaces)
            problems.Add($"faces {faces} but the body had {expectedFaces}");
        if (delta > VolumeTolerancePercent)
            problems.Add($"volume {volume:F3} but the body was {expectedVolume:F3} ({delta:F2} %)");
        if (!valid)
            problems.Add("the imported body fails the solid check");

        // Solid *count* is not part of the verdict: the STEP reader may legitimately regroup shells
        // (one shell inside another comes back as a single solid with a void). Measured on a real
        // part: 2 MANIFOLD_SOLID_BREP entities written, 12,767 faces and 828,713.595 mm3 read back
        // unchanged, 1 solid reported. Geometry intact, grouping different - so it is reported, not
        // failed. A body genuinely lost would change the face count and the volume.
        string regrouped = solids == expectedSolids ? "" : $" (solids regrouped {expectedSolids} -> {solids})";

        return new ArtifactCheck(
            problems.Count == 0 ? ArtifactStatus.Verified : ArtifactStatus.Mismatch,
            faces, solids, volume, delta, valid,
            (problems.Count == 0 ? "ok" : string.Join("; ", problems)) + regrouped);
    }

    private static bool TryRead(string path, out TopoDS_Shape shape)
    {
        bool isIges = path.EndsWith(".igs", StringComparison.OrdinalIgnoreCase)
                   || path.EndsWith(".iges", StringComparison.OrdinalIgnoreCase);

        if (isIges)
        {
            var iges = new IGESControl_Reader();
            iges.ReadFile(path);
            if (iges.TransferRoots() == 0 || iges.NbShapes == 0)
            {
                shape = default!;
                return false;
            }
            shape = iges.OneShape();
            return true;
        }

        var step = new STEPControl_Reader();
        if (step.ReadFile(path) != IFSelect_ReturnStatus.IFSelect_RetDone)
        {
            shape = default!;
            return false;
        }
        if (step.TransferRoots() == 0)
        {
            shape = default!;
            return false;
        }
        shape = step.OneShape();
        return true;
    }

    private static int Count(TopoDS_Shape shape, TopAbs_ShapeEnum kind)
    {
        int count = 0;
        var explorer = new TopExp_Explorer(shape, kind);
        while (explorer.More)
        {
            count++;
            explorer.Next();
        }
        return count;
    }
}
