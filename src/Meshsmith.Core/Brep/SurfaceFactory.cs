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
        _ => throw new NotSupportedException($"no surface for {prim.Kind}")
    };

    public static gp_Ax3 Ax3(Vec3 origin, Vec3 axis) =>
        new(origin.ToPnt(), new gp_Dir(axis.X, axis.Y, axis.Z));
}
