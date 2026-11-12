using Meshsmith.Core.Fit;
using Meshsmith.Core.Geometry;
using Occt;

namespace Meshsmith.Core.Brep;

public static class UvProjector
{
    /// <summary>Projects 3D loop points onto the surface and returns continuous (u,v).</summary>
    public static (double U, double V)[] Project(Geom_Surface surface, IReadOnlyList<Vec3> points)
    {
        var result = new (double U, double V)[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            var proj = new GeomAPI_ProjectPointOnSurf(points[i].ToPnt(), surface);
            if (!proj.IsDone || proj.NbPoints < 1)
                throw new InvalidOperationException("projection onto fitted surface failed");

            proj.Parameters(1, out double u, out double v);
            result[i] = (u, v);
        }

        // Unwrap periodic U so a full revolution becomes a 2*pi span, not a jump to 0.
        if (surface.IsUPeriodic)
        {
            double period = surface.UPeriod;
            for (int i = 1; i < result.Length; i++)
            {
                double du = result[i].U - result[i - 1].U;
                while (du > period / 2) { result[i].U -= period; du = result[i].U - result[i - 1].U; }
                while (du < -period / 2) { result[i].U += period; du = result[i].U - result[i - 1].U; }
            }
        }

        // Close the loop explicitly. On a periodic surface the closing point must be the
        // representative of the first point NEXT TO the last one (u = 2*pi for a full band),
        // not u = 0, or the pcurve jumps backwards across the whole parameter range.
        var first = result[0];
        var last = result[^1];
        double closeU = first.U;
        if (surface.IsUPeriodic)
        {
            double period = surface.UPeriod;
            closeU = first.U + Math.Round((last.U - first.U) / period) * period;
            if (closeU <= last.U) closeU += period;
        }
        return result.Append((closeU, first.V)).ToArray();
    }

    /// <summary>
    /// Tolerance in parameter space. On a cylinder/cone the u axis is an angle, so a 3D
    /// error of `tol` at radius r is `tol / r` radians. `meanRadius` is the region's mean
    /// distance to the axis, computed by the caller.
    /// </summary>
    public static double UvTolerance(Primitive prim, double tol, double meanRadius) => prim switch
    {
        PlanePrimitive => tol,
        CylinderPrimitive c => tol / Math.Max(c.Radius, 1e-6),
        ConePrimitive => tol / Math.Max(meanRadius, 1e-3),
        _ => tol
    };
}
