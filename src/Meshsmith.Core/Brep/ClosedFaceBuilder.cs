using Meshsmith.Core.Fit;
using Meshsmith.Core.Geometry;
using Meshsmith.Core.Mesh;
using Occt;

namespace Meshsmith.Core.Brep;

/// <summary>
/// Faces for a region with no boundary loop - the closed body a torus or a sphere is when it is a
/// part of its own. FaceBuilder needs wires, and BoundaryLoops rightly reports none, so the trim has
/// to come from the parameter space instead: the region's own vertices give the uv rectangle, and the
/// rectangle is split once per periodic direction.
///
/// Measured on R=10 bodies: one natural-bounds face sews to ZERO shells (invalid), while 1x2, 2x1 and
/// 2x2 all sew to one valid shell with the exact volume (torus 1776.528792, sphere 4188.790205).
/// </summary>
public static class ClosedFaceBuilder
{
    /// <summary>The default: one split per periodic direction. Returns 2 faces.</summary>
    public static List<TopoDS_Face> BuildClosed(Primitive prim, double sewTol, Vec3 samplePoint, Vec3 sampleNormal) =>
        Build(prim, SurfaceFactory.Create(prim), ((double Lo, double Hi)?)null, ((double Lo, double Hi)?)null,
              sewTol, samplePoint, sampleNormal);

    /// <summary>The pipeline entry point: measures the uv box from the region's own vertices.</summary>
    public static List<TopoDS_Face> Build(Primitive prim, Geom_Surface surface, MeshData mesh, int[] triangles,
                                          double tol, Vec3 samplePoint, Vec3 sampleNormal)
    {
        var pts = triangles.SelectMany(t => new[] { mesh.V0(t), mesh.V1(t), mesh.V2(t) }).ToArray();
        var uv = UvProjector.Project(surface, pts);

        double uMin = uv.Min(p => p.U), uMax = uv.Max(p => p.U);
        double vMin = uv.Min(p => p.V), vMax = uv.Max(p => p.V);
        // The mesh samples a closed surface from outside one period window, so the projected span is
        // already the natural bounds; clamp it so a stray cap cannot ask for more than one period.
        double uPeriod = surface.UPeriod > 0 ? surface.UPeriod : uMax - uMin;
        double vPeriod = surface.VPeriod > 0 ? surface.VPeriod : vMax - vMin;

        // A CLOSED region has no boundary loop, so its faces have to close on each other: in a
        // parameter the surface reports CLOSED, the region covers the whole period and the box must say
        // so. Measured on the two-torus fixture: the sampled v span stops one facet short of 2*pi, so
        // each face kept two open v edges - 8 free edges for two bodies and the whole part faceted.
        if (surface.IsUClosed && surface.UPeriod > 0) { uMin = 0; uMax = surface.UPeriod; }
        else if (uPeriod > 0) { uMin = Math.Max(uMin, -1e-9); uMax = Math.Min(uMax, uPeriod + 1e-9); }

        if (surface.IsVClosed && surface.VPeriod > 0) { vMin = 0; vMax = surface.VPeriod; }
        else if (!surface.IsVPeriodic) { vMin = Math.Max(vMin, -Math.PI / 2); vMax = Math.Min(vMax, Math.PI / 2); }
        else if (vPeriod > 0) { vMin = Math.Max(vMin, -1e-9); vMax = Math.Min(vMax, vPeriod + 1e-9); }

        return Build(prim, surface, (uMin, uMax), (vMin, vMax), tol, samplePoint, sampleNormal);
    }

    private static List<TopoDS_Face> Build(Primitive prim, Geom_Surface surface,
                                           (double Lo, double Hi)? u, (double Lo, double Hi)? v,
                                           double tol, Vec3 samplePoint, Vec3 sampleNormal)
    {
        double u0 = u?.Lo ?? 0, u1 = u?.Hi ?? (surface.UPeriod > 0 ? surface.UPeriod : 2 * Math.PI);
        // A sphere's V is LATITUDE: its natural bounds are [-pi/2, pi/2] and VPeriod (pi) is the LENGTH
        // of that range, not its upper end. Using VPeriod as the bound would run the face from -pi/2 to
        // +pi, i.e. round the north pole and back down - measured as 6 faces and an invalid shell.
        double v0 = v?.Lo ?? (surface.IsVPeriodic ? 0 : -Math.PI / 2);
        double v1 = v?.Hi ?? (surface.IsVPeriodic
            ? (surface.VPeriod > 0 ? surface.VPeriod : 2 * Math.PI)
            : Math.PI / 2);

        int nu = Pieces(u0, u1, surface.UPeriod > 0 ? surface.UPeriod : double.MaxValue);
        // V is never split here. Measured on R=10 bodies (§2.6 ladder): 2x1 (U split, V whole) sews
        // to one valid shell with the exact volume for BOTH a torus and a sphere, which is the same
        // "one face per region" shape BuildPeriodicBand gives a cylinder band. 2x2 is equally exact
        // but doubles the face count for nothing.
        const int nv = 1;

        var result = new List<TopoDS_Face>(nu * nv);
        int? orientation = null;
        for (int i = 0; i < nu; i++)
            for (int j = 0; j < nv; j++)
            {
                var maker = new BRepBuilderAPI_MakeFace();
                maker.Init(surface, u0 + (u1 - u0) * i / nu, u0 + (u1 - u0) * (i + 1) / nu,
                           v0 + (v1 - v0) * j / nv, v0 + (v1 - v0) * (j + 1) / nv, 1e-7);
                if (!maker.IsDone) throw new InvalidOperationException($"closed face build failed: {maker.Error}");

                if (orientation is null)
                {
                    var proj = new GeomAPI_ProjectPointOnSurf(samplePoint.ToPnt(), surface);
                    if (proj.IsDone && proj.NbPoints > 0)
                    {
                        proj.Parameters(1, out double pu, out double pv);
                        orientation = Vec3.Dot(prim.NormalAt(Vec3.From(surface.Value(pu, pv))), sampleNormal) < 0 ? -1 : 1;
                    }
                }
                var face = maker.Face;
                if (orientation < 0) face.Reverse();
                result.Add(face);
            }
        return result;
    }

    /// <summary>
    /// Splits a span into at most half-period pieces. Measured: a full period in ONE face sews to zero
    /// shells because its seam edge is unpaired; half a period per face is what BuildPeriodicBand
    /// already relies on for cylinders.
    /// </summary>
    private static int Pieces(double lo, double hi, double period)
    {
        double span = hi - lo;
        if (span <= 0 || period <= 0 || period == double.MaxValue) return 1;
        return Math.Max(1, (int)Math.Ceiling(span / (period / 2) - 1e-9));
    }
}
