using Occt;

namespace Meshsmith.Core.Brep;

public sealed record SolidResult(TopoDS_Shape Shape, int Shells, int FreeEdges, bool IsValid, double Volume, int Faces);

public static class SolidBuilder
{
    public static SolidResult Sew(IEnumerable<TopoDS_Face> faces, double tolerance, bool unify = true)
    {
        var list = faces.ToList();
        if (list.Count == 0) throw new InvalidOperationException("no faces to sew");

        var sewing = new BRepBuilderAPI_Sewing(tolerance);
        foreach (var f in list) sewing.Add(f);
        sewing.Perform();

        var sewed = sewing.SewedShape;
        int freeEdges = sewing.NbFreeEdges;

        var shells = new List<TopoDS_Shell>();
        var ex = new TopExp_Explorer(sewed, TopAbs_ShapeEnum.TopAbs_SHELL);
        while (ex.More) { shells.Add((TopoDS_Shell)ex.Current); ex.Next(); }

        // A seam edge of a periodic face is used twice by that same face; sewing counts it
        // as free. Do not fail on that: build the solid and let BRepCheck decide.
        if (shells.Count == 0)
            return new SolidResult(sewed, 0, freeEdges, false, 0, 0);

        TopoDS_Shape shape;
        if (shells.Count == 1)
        {
            var maker = new BRepBuilderAPI_MakeSolid(shells[0]);
            if (!maker.IsDone) return new SolidResult(sewed, 1, freeEdges, false, 0, 0);
            shape = maker.Solid;
        }
        else
        {
            var builder = new BRep_Builder();
            builder.MakeCompound(out var compound);
            foreach (var s in shells)
            {
                var maker = new BRepBuilderAPI_MakeSolid(s);
                if (!maker.IsDone) return new SolidResult(sewed, shells.Count, freeEdges, false, 0, 0);
                builder.Add(compound, maker.Solid);
            }
            shape = compound;
        }

        BRepGProp.VolumeProperties(shape, out var props);
        if (props.Mass < 0)
        {
            // sewn shells can come out inverted; fix by reversing, not by trusting a flag
            shape.Reverse();
            BRepGProp.VolumeProperties(shape, out props);
        }

        // Merge the half-period patches of periodic surfaces back into one face each - but only on a
        // body that is already valid. ShapeUpgrade_UnifySameDomain has taken an access violation out
        // of the whole process on real parts (exit code 0xC0000005), and every body it died on was
        // one the sews had left broken anyway. Unifying a broken body buys nothing; the pipeline
        // replaces it, so the pass is skipped there and the crash never happens.
        //
        // `unify: false` is for a CLOSED torus/sphere body. Measured on a two-torus part: merging its
        // two half-period torus faces gives ONE face periodic in both parameters that BRepCheck calls
        // valid, but the STEP writer transfers nothing for it and the export reads back as an empty
        // file (1,675 bytes, "no transferable shape"), while the same faces left unmerged write
        // 14,671 bytes and verify. The merge is an optimisation; the unmerged patches are exact.
        var validity = new BRepCheck_Analyzer(shape);
        if (unify && validity.IsValid())
        {
            var unifier = new ShapeUpgrade_UnifySameDomain(shape, true, true, false);
            unifier.Build();
            shape = MergeOrKeepExact(unifier.Shape, shape);
            BRepGProp.VolumeProperties(shape, out props);
        }

        bool valid = new BRepCheck_Analyzer(shape).IsValid();
        return new SolidResult(shape, shells.Count, freeEdges, valid, props.Mass, CountFaces(shape));
    }

    /// <summary>
    /// Takes the merged shape only if it passes the checker. The merge can BUILD a defect the facets
    /// did not have: a coplanar region that is not a disc is merged into one face whose boundary
    /// touches itself, and BRepCheck reports the wire as self-intersecting and the face as
    /// unorientable. Measured on clamp.stl body 0 - the exact 5,076-facet shell is valid, the merged
    /// 3,788-face solid is not. The old code assigned the merged shape unchecked, so the pipeline
    /// discarded a valid faceted body and the part exited 5.
    ///
    /// A per-shell ShapeFix_Shell repairs it (FixFaceMode is what rebuilds the merged face's wires;
    /// ShapeFix_Shape alone does not, measured). When even that fails, the exact facets are returned:
    /// they are closed and valid - that is the precondition for getting here - so the fallback can
    /// only be more correct than a broken merge.
    /// </summary>
    internal static TopoDS_Shape MergeOrKeepExact(TopoDS_Shape merged, TopoDS_Shape exact)
    {
        if (new BRepCheck_Analyzer(merged).IsValid()) return merged;

        var repaired = new List<TopoDS_Shape>();
        var shells = new TopExp_Explorer(merged, TopAbs_ShapeEnum.TopAbs_SHELL);
        while (shells.More)
        {
            var fixer = new ShapeFix_Shell((TopoDS_Shell)shells.Current)
            {
                FixFaceMode = 1,
                FixOrientationMode = 1
            };
            fixer.Perform();

            var fixedShells = new List<TopoDS_Shell>();
            var inner = new TopExp_Explorer(fixer.Shape, TopAbs_ShapeEnum.TopAbs_SHELL);
            while (inner.More) { fixedShells.Add((TopoDS_Shell)inner.Current); inner.Next(); }

            if (fixedShells.Count == 1)
            {
                var maker = new BRepBuilderAPI_MakeSolid(fixedShells[0]);
                repaired.Add(maker.IsDone ? maker.Solid : fixer.Shape);
            }
            else repaired.Add(fixer.Shape);

            shells.Next();
        }

        if (repaired.Count == 0) return exact;

        TopoDS_Shape candidate;
        if (repaired.Count == 1) candidate = repaired[0];
        else
        {
            var builder = new BRep_Builder();
            builder.MakeCompound(out var compound);
            foreach (var one in repaired) builder.Add(compound, one);
            candidate = compound;
        }

        return new BRepCheck_Analyzer(candidate).IsValid() ? candidate : exact;
    }

    /// <summary>
    /// Sews at the requested tolerance and, when that leaves the shell open, retries at 2x, 4x and 8x.
    /// A crack wider than the tolerance is the one failure a single sew cannot see: BRepBuilderAPI_Sewing
    /// already cuts an edge at a T-junction vertex that lies on it (measured), and ShapeFix_Shape repairs
    /// nothing on a sewn shell (measured: Perform() returns false), so the sewing tolerance is the only
    /// knob that closes a real gap. Returns the best attempt, valid-and-closed if any rung got there.
    /// </summary>
    public static SolidResult SewRobust(IEnumerable<TopoDS_Face> faces, double tolerance, bool unify = true)
    {
        var list = faces.ToList();
        var best = Sew(list, tolerance, unify);
        if (best.IsValid && best.FreeEdges == 0) return best;

        // No free edges means there is nothing left for a wider tolerance to stitch: the seams are
        // already joined and the remaining defect is topological (a non-manifold edge, an inverted
        // face), which re-sewing cannot repair. The faceted fallback lands exactly here, and each
        // rung it burned on clamp.stl re-sewed 5,784 faces for 2.2 s with nothing to show for it.
        if (best.FreeEdges == 0) return best;

        foreach (double factor in new[] { 2.0, 4.0, 8.0 })
        {
            var attempt = Sew(list, tolerance * factor, unify);
            if (attempt.IsValid && attempt.FreeEdges == 0) return attempt;
            if (attempt.FreeEdges < best.FreeEdges || (attempt.IsValid && !best.IsValid)) best = attempt;
        }
        return best;
    }

    private static int CountFaces(TopoDS_Shape shape)
    {
        int n = 0;
        var ex = new TopExp_Explorer(shape, TopAbs_ShapeEnum.TopAbs_FACE);
        while (ex.More) { n++; ex.Next(); }
        return n;
    }
}
