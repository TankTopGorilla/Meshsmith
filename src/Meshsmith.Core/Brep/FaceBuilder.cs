using Meshsmith.Core.Fit;
using Meshsmith.Core.Geometry;
using Occt;

namespace Meshsmith.Core.Brep;

public static class FaceBuilder
{
    public static List<TopoDS_Face> Build(Geom_Surface surface, Primitive prim,
                                    IReadOnlyList<(double U, double V)[]> uvLoops,
                                    double uvTol, Vec3 samplePoint, Vec3 sampleNormal)
    {
        if (uvLoops.Count == 0) throw new InvalidOperationException("face has no boundary");

        // A face that wraps a full revolution (cylinder/cone band) is split into half-period
        // patches: a single natural-bounds patch has an unpaired seam edge and will not sew.
        if (surface.IsUPeriodic && uvLoops.Any(l => SpansFullPeriod(l, surface)))
            return BuildPeriodicBand(prim, uvLoops, samplePoint, sampleNormal);

        var wires = new List<TopoDS_Wire>();
        foreach (var loop in uvLoops) wires.Add(BuildWire(surface, loop, uvTol));

        // Outer wire = largest |signed area| in parameter space; ensure CCW, holes CW.
        var order = Enumerable.Range(0, uvLoops.Count)
            .OrderByDescending(i => Math.Abs(SignedArea(uvLoops[i]))).ToArray();

        var outer = Orient(wires[order[0]], uvLoops[order[0]], ccw: true);
        var holes = order.Skip(1).Select(i => Orient(wires[i], uvLoops[i], ccw: false)).ToList();

        TopoDS_Face? face = null;
        if (holes.Count == 0)
        {
            var maker0 = new BRepBuilderAPI_MakeFace(surface, outer);
            if (!maker0.IsDone) throw new InvalidOperationException($"face build failed: {maker0.Error}");
            face = maker0.Face;
        }
        else
        {
            // Whether a wire becomes a hole depends on its orientation relative to the
            // surface normal. Try both and keep the candidate that actually subtracts
            // (smaller area); a missed hole reports outer+inner.
            foreach (var flip in new[] { false, true })
            {
                var maker = new BRepBuilderAPI_MakeFace(surface, outer);
                foreach (var h in holes) maker.Add(flip ? (TopoDS_Wire)h.Reversed : h);
                if (!maker.IsDone) continue;
                var candidate = maker.Face;
                if (face is null || candidate.Area < face.Area) face = candidate;
            }
            if (face is null) throw new InvalidOperationException("face with holes could not be built");
        }

        // Orient against the MESH, not the region average: the average normal of a closed
        // cylinder band is ~zero, so it cannot decide the orientation.
        if (Vec3.Dot(SurfaceNormal(surface, prim, samplePoint), sampleNormal) < 0) face.Reverse();
        return new List<TopoDS_Face> { face };
    }

    /// <summary>Normal of the fitted surface at the projection of a known mesh point.</summary>
    private static Vec3 SurfaceNormal(Geom_Surface surface, Primitive prim, Vec3 p)
    {
        var proj = new GeomAPI_ProjectPointOnSurf(p.ToPnt(), surface);
        if (!proj.IsDone || proj.NbPoints < 1) return prim.NormalAt(p);
        proj.Parameters(1, out double u, out double v);
        return prim.NormalAt(Vec3.From(surface.Value(u, v)));
    }

    private static bool SpansFullPeriod((double U, double V)[] loop, Geom_Surface surface)
    {
        double min = loop.Min(p => p.U), max = loop.Max(p => p.U);
        return (max - min) >= 0.75 * surface.UPeriod;
    }

    private static List<TopoDS_Face> BuildPeriodicBand(Primitive prim, IReadOnlyList<(double U, double V)[]> loops,
                                                       Vec3 samplePoint, Vec3 sampleNormal)
    {
        // All loops of a band must share ONE period window; each loop is unwrapped from its
        // own arbitrary projection angle, so taking a global min/max would span several periods.
        var spanLoop = loops.First(l => SpansFullPeriod(l, SurfaceFactory.Create(prim)));
        double uMin = spanLoop.Min(p => p.U);
        double uMax = uMin + SurfaceFactory.Create(prim).UPeriod;
        double vMin = loops.Min(l => l.Min(p => p.V));
        double vMax = loops.Max(l => l.Max(p => p.V));

        // Half-period patches: at most PI of u per face, so every seam is shared with the
        // neighbouring patch (a full 2*PI patch leaves its seam edge unpaired).
        int pieces = Math.Max(1, (int)Math.Ceiling((uMax - uMin) / Math.PI));
        double step = (uMax - uMin) / pieces;
        var sharedSurface = SurfaceFactory.Create(prim);   // one handle: UnifySameDomain needs IsSame

        var result = new List<TopoDS_Face>(pieces);
        for (int i = 0; i < pieces; i++)
        {
            double u0 = uMin + i * step;
            double u1 = u0 + step;

            var maker = new BRepBuilderAPI_MakeFace();
            maker.Init(sharedSurface, u0, u1, Math.Min(vMin, vMax), Math.Max(vMin, vMax), 1e-7);

            if (!maker.IsDone) throw new InvalidOperationException($"periodic face build failed: {maker.Error}");
            var face = maker.Face;
            if (Vec3.Dot(SurfaceNormal(sharedSurface, prim, samplePoint), sampleNormal) < 0)
                face.Reverse();
            result.Add(face);
        }
        return result;
    }

    private static TopoDS_Wire BuildWire(Geom_Surface surface, (double U, double V)[] loop, double uvTol)
    {
        var segments = CurveFitter2d.FitLoop(loop, uvTol, cornerDeg: 20);
        if (segments.Count == 0) throw new InvalidOperationException("empty boundary");

        // One shared vertex per segment boundary; the last edge closes onto the first vertex.
        var vertices = new TopoDS_Vertex[segments.Count];
        for (int i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            vertices[i] = new BRepBuilderAPI_MakeVertex(surface.Value(s.Start.U, s.Start.V)).Vertex;
        }

        var maker = new BRepBuilderAPI_MakeWire();
        for (int i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            var v1 = vertices[i];
            var v2 = vertices[(i + 1) % segments.Count];
            var bounded = new Geom2d_TrimmedCurve(s.Curve, s.First, s.Last);

            var edgeMaker = segments.Count == 1 && v1.IsSame(v2)
                ? new BRepBuilderAPI_MakeEdge(bounded, surface, s.First, s.Last)
                : new BRepBuilderAPI_MakeEdge(bounded, surface, v1, v2);
            if (!edgeMaker.IsDone) throw new InvalidOperationException(
                $"edge build failed: {edgeMaker.Error} (seg {i}/{segments.Count} {s.Curve.GetType().Name} " +
                $"({s.Start.U:F3},{s.Start.V:F3})->({s.End.U:F3},{s.End.V:F3}) p=({s.First:F3},{s.Last:F3}) closed={v1.IsSame(v2)})");
            maker.Add(edgeMaker.Edge);
        }

        if (!maker.IsDone) throw new InvalidOperationException($"wire build failed: {maker.Error}");
        var wire = maker.Wire;
        BRepLib.BuildCurves3d(wire);
        return wire;
    }

    private static TopoDS_Wire Orient(TopoDS_Wire wire, (double U, double V)[] uv, bool ccw)
    {
        double area = SignedArea(uv);
        bool isCcw = area > 0;
        return isCcw == ccw ? wire : (TopoDS_Wire)wire.Reversed;
    }

    private static double SignedArea((double U, double V)[] p)
    {
        double a = 0;
        for (int i = 0; i < p.Length; i++)
        {
            var q = p[(i + 1) % p.Length];
            a += p[i].U * q.V - q.U * p[i].V;
        }
        return a / 2;
    }
}
