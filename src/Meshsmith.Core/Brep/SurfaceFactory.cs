using Meshsmith.Core.Fit;
using Meshsmith.Core.Geometry;
using Occt;

namespace Meshsmith.Core.Brep;

public static class SurfaceFactory
{
    public static Geom_Surface Create(Primitive prim) => prim switch
    {
        PlanePrimitive p => new Geom_Plane(p.Point.ToPnt(), new gp_Dir(p.Normal.X, p.Normal.Y, p.Normal.Z)),
        CylinderPrimitive c => new Geom_CylindricalSurface(Ax3(c.PointOnAxis, c.Axis), c.Radius),
        // Reference circle 1 mm from the apex avoids a degenerate zero-radius cone while
        // keeping the same infinite surface (gp_Cone radius at v is RefRadius + v*tan(angle)).
        ConePrimitive k => new Geom_ConicalSurface(
            Ax3(k.Apex + k.Axis * 1.0, k.Axis), k.HalfAngle, Math.Tan(k.HalfAngle)),
        // A torus is periodic in BOTH parameters (measured: UPeriod = VPeriod = 2 pi, IsUClosed and
        // IsVClosed both true); a sphere is periodic in U only, with singular poles at v = +-pi/2.
        TorusPrimitive t => new Geom_ToroidalSurface(Ax3(t.PointOnAxis, t.Axis), t.MajorRadius, t.MinorRadius),
        // The sphere's Ax3 picks Z as its axis, so a fitted centre with no axis is enough: a sphere has
        // no oriented axis, and the projection is invariant to the frame's rotation about it.
        SpherePrimitive s => new Geom_SphericalSurface(Ax3(s.Center, new Vec3(0, 0, 1)), s.Radius),
        _ => throw new NotSupportedException($"no surface for {prim.Kind}")
    };

    public static gp_Ax3 Ax3(Vec3 origin, Vec3 axis) =>
        new(origin.ToPnt(), new gp_Dir(axis.X, axis.Y, axis.Z));
}
