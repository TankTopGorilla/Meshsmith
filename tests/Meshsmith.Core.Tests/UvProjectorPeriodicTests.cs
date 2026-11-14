using Meshsmith.Core.Brep;
using Meshsmith.Core.Fit;
using Meshsmith.Core.Geometry;
using Occt;
using Xunit;

namespace Meshsmith.Core.Tests;

/// <summary>
/// UvProjector must return the loop's REAL parameter span. It used to append a closing point one full
/// period past the first point whatever the loop did, so a 4-quad cylinder patch projected a 2*pi
/// span and FaceBuilder routed it into BuildPeriodicBand - 12x the correct area (measured).
/// </summary>
public class UvProjectorPeriodicTests
{
    private static Geom_Surface Cylinder() =>
        SurfaceFactory.Create(new CylinderPrimitive
        { PointOnAxis = Vec3.Zero, Axis = new Vec3(0, 0, 1), Radius = 10 });

    // Built directly rather than through SurfaceFactory: the factory's torus arm is the face-build
    // task's edit, and the projector is a surface-level concern - it only needs a V-periodic surface.
    private static Geom_Surface Torus() =>
        new Geom_ToroidalSurface(SurfaceFactory.Ax3(Vec3.Zero, new Vec3(0, 0, 1)), 10, 3);

    /// <summary>A 30-degree arc of a cylinder's side: a patch, not a band.</summary>
    [Fact]
    public void A_patch_keeps_its_own_u_span_and_does_not_gain_a_period()
    {
        var surface = Cylinder();
        var pts = new List<Vec3>();
        for (int i = 0; i <= 8; i++)
        {
            double a = Math.PI / 6 * i / 8;
            pts.Add(new Vec3(10 * Math.Cos(a), 10 * Math.Sin(a), 0));
        }
        for (int i = 8; i >= 0; i--)
        {
            double a = Math.PI / 6 * i / 8;
            pts.Add(new Vec3(10 * Math.Cos(a), 10 * Math.Sin(a), 5));
        }
        pts.Add(pts[0]);

        var uv = UvProjector.Project(surface, pts);
        double span = uv.Max(p => p.U) - uv.Min(p => p.U);
        Assert.InRange(span, Math.PI / 6 * 0.99, Math.PI / 6 * 1.01);
        // the closing point is the first point's representative NEXT TO the last, not a period later
        Assert.True(Math.Abs(uv[^1].U - uv[0].U) <= Math.PI / 6 + 1e-9);
    }

    /// <summary>A full ring MUST keep its 2*pi span - that is what BuildPeriodicBand needs.</summary>
    [Fact]
    public void A_full_ring_keeps_one_whole_period()
    {
        var surface = Cylinder();
        var pts = new List<Vec3>();
        for (int i = 0; i <= 48; i++)
        {
            double a = 2 * Math.PI * i / 48;
            pts.Add(new Vec3(10 * Math.Cos(a), 10 * Math.Sin(a), 0));
        }
        for (int i = 48; i >= 0; i--)
        {
            double a = 2 * Math.PI * i / 48;
            pts.Add(new Vec3(10 * Math.Cos(a), 10 * Math.Sin(a), 5));
        }
        pts.Add(pts[0]);

        var uv = UvProjector.Project(surface, pts);
        Assert.InRange(uv.Max(p => p.U) - uv.Min(p => p.U), 2 * Math.PI * 0.99, 2 * Math.PI * 1.01);
    }

    /// <summary>
    /// A torus patch crossing the tube's V seam must come back CONTINUOUS in v. Before the fix the
    /// projector unwrapped u only. Measured: OCCT projects a toroidal surface's v into [0, 2*pi), so
    /// the seam is at v = 0 - a patch spanning -30..30 degrees projects as 5.7596, 0.0000, 0.5236 and
    /// comes back with a 5.76 rad span for a 1.05 rad patch.
    /// </summary>
    [Fact]
    public void A_torus_patch_across_the_v_seam_is_unwrapped_in_v()
    {
        var surface = Torus();
        var pts = new List<Vec3>();
        // v from -30 to +30 degrees (the V seam), u from 0 to 60 degrees, as a closed loop
        double[] vs = new[] { -30, 0, 30 }.Select(d => Math.PI * d / 180).ToArray();
        double[] us = new[] { 0, 30, 60 }.Select(d => Math.PI * d / 180).ToArray();
        foreach (var v in vs) pts.Add(TorusPoint(us[0], v));
        foreach (var u in us) pts.Add(TorusPoint(u, vs[^1]));
        foreach (var v in vs.Reverse()) pts.Add(TorusPoint(us[^1], v));
        foreach (var u in us.Reverse()) pts.Add(TorusPoint(u, vs[0]));

        var uv = UvProjector.Project(surface, pts);
        Assert.InRange(uv.Max(p => p.V) - uv.Min(p => p.V), Math.PI / 3 * 0.99, Math.PI / 3 * 1.01);
    }

    private static Vec3 TorusPoint(double u, double v) =>
        new((10 + 3 * Math.Cos(v)) * Math.Cos(u), (10 + 3 * Math.Cos(v)) * Math.Sin(u), 3 * Math.Sin(v));
}
