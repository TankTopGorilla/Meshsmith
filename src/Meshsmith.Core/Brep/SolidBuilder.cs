using Occt;

namespace Meshsmith.Core.Brep;

public sealed record SolidResult(TopoDS_Shape Shape, int Shells, int FreeEdges, bool IsValid, double Volume, int Faces);

public static class SolidBuilder
{
    public static SolidResult Sew(IEnumerable<TopoDS_Face> faces, double tolerance)
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
        var validity = new BRepCheck_Analyzer(shape);
        if (validity.IsValid())
        {
            var unifier = new ShapeUpgrade_UnifySameDomain(shape, true, true, false);
            unifier.Build();
            shape = unifier.Shape;
            BRepGProp.VolumeProperties(shape, out props);
        }

        bool valid = new BRepCheck_Analyzer(shape).IsValid();
        return new SolidResult(shape, shells.Count, freeEdges, valid, props.Mass, CountFaces(shape));
    }

    private static int CountFaces(TopoDS_Shape shape)
    {
        int n = 0;
        var ex = new TopExp_Explorer(shape, TopAbs_ShapeEnum.TopAbs_FACE);
        while (ex.More) { n++; ex.Next(); }
        return n;
    }
}
