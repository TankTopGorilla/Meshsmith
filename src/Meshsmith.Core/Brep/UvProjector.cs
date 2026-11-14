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

        // Unwrap every periodic parameter, so a full revolution becomes a 2*pi span, not a jump to 0.
        Unwrap(result, surface.IsUPeriodic ? surface.UPeriod : 0, u: true);
        Unwrap(result, surface.IsVPeriodic ? surface.VPeriod : 0, u: false);

        // Close the loop explicitly. On a periodic surface the closing point must be the
        // representative of the first point NEXT TO the last one (u = 2*pi for a full band), not
        // u = 0, or the pcurve jumps backwards across the whole parameter range. Only a loop that
        // really wraps gets the extra period: an unconditional one projected a 30-degree cylinder
        // patch as a full band (12x its area) and sent it into BuildPeriodicBand.
        var first = result[0];
        var last = result[^1];
        double closeU = ClosePoint(first.U, last.U, surface.IsUPeriodic ? surface.UPeriod : 0, result, u: true);
        double closeV = ClosePoint(first.V, last.V, surface.IsVPeriodic ? surface.VPeriod : 0, result, u: false);
        return result.Append((closeU, closeV)).ToArray();
    }

    /// <summary>Unwrap one parameter across its seam, in place. A zero period means "not periodic".</summary>
    private static void Unwrap((double U, double V)[] uv, double period, bool u)
    {
        if (period <= 0) return;
        for (int i = 1; i < uv.Length; i++)
        {
            double d = u ? uv[i].U - uv[i - 1].U : uv[i].V - uv[i - 1].V;
            while (d > period / 2) { Set(uv, i, u, (u ? uv[i].U : uv[i].V) - period); d = u ? uv[i].U - uv[i - 1].U : uv[i].V - uv[i - 1].V; }
            while (d < -period / 2) { Set(uv, i, u, (u ? uv[i].U : uv[i].V) + period); d = u ? uv[i].U - uv[i - 1].U : uv[i].V - uv[i - 1].V; }
        }
    }

    private static void Set((double U, double V)[] uv, int i, bool u, double value) =>
        uv[i] = u ? (value, uv[i].V) : (uv[i].U, value);

    /// <summary>
    /// The closing representative of <paramref name="first"/> next to <paramref name="last"/>. Adds a
    /// period only when the span the caller has already unwrapped is at least three quarters of one -
    /// the same threshold FaceBuilder.SpansFullPeriod uses, so the two cannot disagree.
    /// </summary>
    private static double ClosePoint(double first, double last, double period, (double U, double V)[] uv, bool u)
    {
        if (period <= 0) return first;
        double span = u ? uv.Max(p => p.U) - uv.Min(p => p.U) : uv.Max(p => p.V) - uv.Min(p => p.V);
        double close = first + Math.Round((last - first) / period) * period;
        if (span >= 0.75 * period && close <= last) close += period;
        return close;
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
        TorusPrimitive t => tol / Math.Max(meanRadius, 1e-3),
        SpherePrimitive => tol / Math.Max(meanRadius, 1e-3),
        _ => tol
    };
}
