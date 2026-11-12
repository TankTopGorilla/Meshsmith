using Meshsmith.Core.Fit;
using Occt;

namespace Meshsmith.Core.Brep;

public sealed record Segment2d(Geom2d_Curve Curve, double First, double Last,
                              (double U, double V) Start, (double U, double V) End);

public static class CurveFitter2d
{
    /// <summary>
    /// Fits a CLOSED uv loop (first point == last point) at corners (turn angle above
    /// cornerDeg) and returns one segment per run. Callers must close the loop first.
    /// </summary>
    public static List<Segment2d> FitLoop(IReadOnlyList<(double U, double V)> loop, double uvTol, double cornerDeg)
    {
        if (loop.Count < 3) throw new ArgumentException("loop too short");

        // drop duplicate consecutive points; the caller has already closed the loop
        var clean = new List<(double U, double V)> { loop[0] };
        for (int i = 1; i < loop.Count; i++)
            if (Dist2(loop[i], clean[^1]) > uvTol * uvTol * 0.01) clean.Add(loop[i]);
        if (clean.Count < 4) throw new ArgumentException("degenerate loop");

        double cosLimit = Math.Cos(cornerDeg * Math.PI / 180.0);
        var runs = new List<List<(double U, double V)>>();
        var current = new List<(double U, double V)> { clean[0] };

        for (int i = 1; i < clean.Count - 1; i++)
        {
            current.Add(clean[i]);
            var prev = clean[i - 1];
            var here = clean[i];
            var next = clean[i + 1];
            double ax = here.U - prev.U, ay = here.V - prev.V;
            double bx = next.U - here.U, by = next.V - here.V;
            double la = Math.Sqrt(ax * ax + ay * ay), lb = Math.Sqrt(bx * bx + by * by);
            double cos = la < 1e-12 || lb < 1e-12 ? 1 : (ax * bx + ay * by) / (la * lb);

            if (cos < cosLimit)
            {
                runs.Add(current);
                current = new List<(double U, double V)> { here };
            }
        }
        current.Add(clean[^1]);
        runs.Add(current);

        var result = new List<Segment2d>();
        foreach (var run in runs)
        {
            if (run.Count < 2) continue;
            result.Add(FitRun(run, uvTol));
        }
        return result;
    }

    private static Segment2d FitRun(List<(double U, double V)> run, double tol)
    {
        var start = run[0];
        var end = run[^1];

        if (TryLine(run, tol, out var line, out double p0, out double p1))
            return new Segment2d(line, p0, p1, start, end);

        if (TryCircle(run, tol, out var circle, out double a0, out double a1))
            return new Segment2d(circle, a0, a1, start, end);

        var arr = new TColgp_Array1OfPnt2d(1, run.Count);
        for (int i = 0; i < run.Count; i++) arr[i + 1] = new gp_Pnt2d(run[i].U, run[i].V);
        var bs = new Geom2dAPI_PointsToBSpline(arr, 3, 8, GeomAbs_Shape.GeomAbs_C2, tol);
        if (!bs.IsDone) throw new InvalidOperationException("2D B-spline fit failed");
        var curve = bs.Curve;
        return new Segment2d(curve, curve.FirstParameter, curve.LastParameter, start, end);
    }

    private static bool TryLine(List<(double U, double V)> run, double tol,
                                out Geom2d_Curve curve, out double p0, out double p1)
    {
        curve = null!; p0 = 0; p1 = 0;
        var a = run[0]; var b = run[^1];
        double dx = b.U - a.U, dy = b.V - a.V;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-9) return false;

        foreach (var p in run)
        {
            double cross = Math.Abs((p.U - a.U) * dy - (p.V - a.V) * dx) / len;
            if (cross > tol) return false;
        }

        curve = new Geom2d_Line(new gp_Pnt2d(a.U, a.V), new gp_Dir2d(dx / len, dy / len));
        p0 = 0; p1 = len;
        return true;
    }

    private static bool TryCircle(List<(double U, double V)> run, double tol,
                                  out Geom2d_Curve curve, out double a0, out double a1)
    {
        curve = null!; a0 = 0; a1 = 0;
        if (run.Count < 4) return false;

        // Kasa algebraic circle fit
        double suu = 0, suv = 0, svv = 0, su = 0, sv = 0, s1 = 0, suw = 0, svw = 0, sw = 0;
        foreach (var p in run)
        {
            double w = p.U * p.U + p.V * p.V;
            suu += p.U * p.U; suv += p.U * p.V; svv += p.V * p.V;
            su += p.U; sv += p.V; s1 += 1;
            suw += p.U * w; svw += p.V * w; sw += w;
        }
        double[,] m = { { suu, suv, su }, { suv, svv, sv }, { su, sv, s1 } };
        if (!Fitters.Solve(m, new[] { -suw, -svw, -sw }, out var x)) return false;

        double cu = -x[0] / 2, cv = -x[1] / 2;
        double r2 = cu * cu + cv * cv - x[2];
        if (r2 <= 0) return false;
        double r = Math.Sqrt(r2);

        foreach (var p in run)
        {
            double d = Math.Abs(Math.Sqrt((p.U - cu) * (p.U - cu) + (p.V - cv) * (p.V - cv)) - r);
            if (d > tol) return false;
        }

        a0 = Math.Atan2(run[0].V - cv, run[0].U - cu);
        a1 = Math.Atan2(run[^1].V - cv, run[^1].U - cu);
        if (a1 <= a0) a1 += 2 * Math.PI;

        curve = new Geom2d_Circle(new gp_Ax2d(new gp_Pnt2d(cu, cv), new gp_Dir2d(1, 0)), r);
        return true;
    }

    private static double Dist2((double U, double V) a, (double U, double V) b)
    {
        double du = a.U - b.U, dv = a.V - b.V;
        return du * du + dv * dv;
    }
}
