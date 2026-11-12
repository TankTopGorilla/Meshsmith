using Meshsmith.Core.Geometry;

namespace Meshsmith.Core.Fit;

public static class Fitters
{
    public static PlanePrimitive? FitPlane(IReadOnlyList<Vec3> pts, double tol)
    {
        if (pts.Count < 3) return null;

        // Pick the frame axis with the smallest spread and fit d = A*u + B*v + C there.
        var (u, v, w) = PickFrame(pts);
        double suu = 0, suv = 0, svv = 0, su = 0, sv = 0, s1 = 0, sw = 0, suw = 0, svw = 0;
        foreach (var p in pts)
        {
            double a = Vec3.Dot(p, u), b = Vec3.Dot(p, v), d = Vec3.Dot(p, w);
            suu += a * a; suv += a * b; svv += b * b;
            su += a; sv += b; s1 += 1;
            sw += d; suw += a * d; svw += b * d;
        }

        double[,] m = { { suu, suv, su }, { suv, svv, sv }, { su, sv, s1 } };
        double[] rhs = { suw, svw, sw };
        if (!Solve(m, rhs, out var abc)) return null;

        var normal = (u * abc[0] + v * abc[1] - w).Normalized();
        var centroid = Vec3.Mean(pts);

        double max = 0, sum = 0;
        foreach (var p in pts)
        {
            double d = Math.Abs(Vec3.Dot(p - centroid, normal));
            max = Math.Max(max, d); sum += d;
            if (max > tol) return null;
        }

        return new PlanePrimitive
        {
            Point = centroid, Normal = normal,
            FitMax = max, FitMean = sum / pts.Count
        };
    }

    public static CylinderPrimitive? FitCylinder(IReadOnlyList<Vec3> pts, IReadOnlyList<Vec3> normals,
                                                 double tol, double diag)
    {
        if (pts.Count < 6) return null;

        // 1) Axis: solve N.A = 0 (least squares, try all three fixed components).
        if (!TryNullDirection(normals, out var axis, out double axisResidual)) return null;
        if (axisResidual > 2e-3) return null;

        // 2) C (in the plane through the origin perpendicular to axis) and r: N.C + r = N.P
        var (e1, e2) = Basis(axis);
        double a11 = 0, a12 = 0, a13 = 0, a22 = 0, a23 = 0, a33 = 0;
        double b1 = 0, b2 = 0, b3 = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            var n = normals[i];
            double n1 = Vec3.Dot(n, e1), n2 = Vec3.Dot(n, e2);
            double rhs = Vec3.Dot(n, pts[i]);
            a11 += n1 * n1; a12 += n1 * n2; a13 += n1;
            a22 += n2 * n2; a23 += n2;
            a33 += 1;
            b1 += n1 * rhs; b2 += n2 * rhs; b3 += rhs;
        }

        double[,] m = { { a11, a12, a13 }, { a12, a22, a23 }, { a13, a23, a33 } };
        if (!Solve(m, new[] { b1, b2, b3 }, out var x)) return null;

        var centre = e1 * x[0] + e2 * x[1];
        double radius = Math.Abs(x[2]);
        if (radius < 1e-6 || radius > 10 * diag) return null;   // a plane is not a cylinder

        double max = 0, sum = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            var d = pts[i] - centre;
            double rho = (d - axis * Vec3.Dot(d, axis)).Length;
            double err = Math.Abs(rho - radius);
            max = Math.Max(max, err); sum += err;
            if (max > tol) return null;
        }

        return new CylinderPrimitive
        {
            PointOnAxis = centre, Axis = axis, Radius = radius,
            FitMax = max, FitMean = sum / pts.Count
        };
    }

    public static ConePrimitive? FitCone(IReadOnlyList<Vec3> pts, IReadOnlyList<Vec3> normals,
                                         double tol, double diag)
    {
        if (pts.Count < 8) return null;

        // normals satisfy N.A = c  (c = -sin(halfAngle) for an outward normal)
        if (!TryOffsetDirection(normals, out var axis, out double c, out double rms)) return null;
        if (rms > 5e-3) return null;

        double sin = Math.Clamp(Math.Abs(c), 0, 1);
        if (sin > 0.98) return null;                        // too flat to be a cone
        double halfAngle = Math.Asin(sin);
        if (halfAngle < 1e-3) return null;

        // apex: N.X = N.P  (every generator through P passes through the apex)
        double a11 = 0, a12 = 0, a13 = 0, a22 = 0, a23 = 0, a33 = 0;
        double b1 = 0, b2 = 0, b3 = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            var p = pts[i]; var n = normals[i];
            a11 += n.X * n.X; a12 += n.X * n.Y; a13 += n.X * n.Z;
            a22 += n.Y * n.Y; a23 += n.Y * n.Z; a33 += n.Z * n.Z;
            double np = Vec3.Dot(n, p);
            b1 += n.X * np; b2 += n.Y * np; b3 += n.Z * np;
        }
        double[,] m = { { a11, a12, a13 }, { a12, a22, a23 }, { a13, a23, a33 } };
        if (!Solve(m, new[] { b1, b2, b3 }, out var x)) return null;
        var apex = new Vec3(x[0], x[1], x[2]);

        double max = 0, sum = 0;
        foreach (var p in pts)
        {
            var d = p - apex;
            double z = Vec3.Dot(d, axis);
            double rho = (d - axis * z).Length;
            double err = Math.Abs(rho - Math.Abs(z) * Math.Tan(halfAngle));
            max = Math.Max(max, err); sum += err;
            if (max > tol) return null;
        }
        if ((apex - Vec3.Mean(pts)).Length > 4 * diag) return null;

        return new ConePrimitive
        {
            Apex = apex, Axis = axis, HalfAngle = halfAngle,
            FitMax = max, FitMean = sum / pts.Count
        };
    }

    // Smallest-eigenvector direction of sum(n n^T), computed by fixing each component in turn.
    internal static bool TryNullDirection(IReadOnlyList<Vec3> normals, out Vec3 dir, out double residual)
    {
        dir = Vec3.Zero; residual = double.MaxValue;
        for (int k = 0; k < 3; k++)
        {
            int i1 = (k + 1) % 3, i2 = (k + 2) % 3;
            double a11 = 0, a12 = 0, a22 = 0, b1 = 0, b2 = 0;
            foreach (var n in normals)
            {
                double n1 = n[i1], n2 = n[i2], nk = n[k];
                a11 += n1 * n1; a12 += n1 * n2; a22 += n2 * n2;
                b1 -= n1 * nk; b2 -= n2 * nk;
            }
            double det = a11 * a22 - a12 * a12;
            if (Math.Abs(det) < 1e-16) continue;

            double s1 = (b1 * a22 - b2 * a12) / det;
            double s2 = (a11 * b2 - a12 * b1) / det;
            var candidate = k switch
            {
                0 => new Vec3(1, s1, s2),
                1 => new Vec3(s1, 1, s2),
                _ => new Vec3(s1, s2, 1),
            };

            candidate = candidate.Normalized();
            double rms = 0;
            foreach (var n in normals) { double d = Vec3.Dot(n, candidate); rms += d * d; }
            rms = Math.Sqrt(rms / normals.Count);
            if (rms < residual) { residual = rms; dir = candidate; }
        }
        return residual < double.MaxValue;
    }

    // Direction with N.A = c (offset least squares), the cone axis.
    internal static bool TryOffsetDirection(IReadOnlyList<Vec3> normals, out Vec3 dir, out double c, out double rms)
    {
        dir = Vec3.Zero; c = 0; rms = double.MaxValue;
        for (int k = 0; k < 3; k++)
        {
            int i1 = (k + 1) % 3, i2 = (k + 2) % 3;
            double a11 = 0, a12 = 0, a13 = 0, a22 = 0, a23 = 0, a33 = 0;
            double b1 = 0, b2 = 0, b3 = 0;
            foreach (var n in normals)
            {
                // unknowns (s1, s2, c) with  n1*s1 + n2*s2 - c = -nk
                double n1 = n[i1], n2 = n[i2], nk = n[k];
                a11 += n1 * n1; a12 += n1 * n2; a13 -= n1;
                a22 += n2 * n2; a23 -= n2;
                a33 += 1;
                b1 -= n1 * nk; b2 -= n2 * nk; b3 += nk;
            }
            double[,] m = { { a11, a12, a13 }, { a12, a22, a23 }, { a13, a23, a33 } };
            if (!Solve(m, new[] { b1, b2, b3 }, out var x)) continue;

            Vec3 candidate = k switch
            {
                0 => new Vec3(1, x[0], x[1]),
                1 => new Vec3(x[0], 1, x[1]),
                _ => new Vec3(x[0], x[1], 1),
            };
            candidate = candidate.Normalized();
            double cc = x[2];
            double err = 0;
            foreach (var n in normals) { double d = Vec3.Dot(n, candidate) - cc; err += d * d; }
            err = Math.Sqrt(err / normals.Count);
            if (err < rms) { rms = err; dir = candidate; c = cc; }
        }
        return rms < double.MaxValue;
    }

    internal static (Vec3 U, Vec3 V, Vec3 W) PickFrame(IReadOnlyList<Vec3> pts)
    {
        var min = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue);
        var max = new Vec3(double.MinValue, double.MinValue, double.MinValue);
        foreach (var p in pts)
        {
            min = new Vec3(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
            max = new Vec3(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
        }
        var e = max - min;
        int drop = e.X <= e.Y && e.X <= e.Z ? 0 : (e.Y <= e.Z ? 1 : 2);
        var axes = new[] { new Vec3(1, 0, 0), new Vec3(0, 1, 0), new Vec3(0, 0, 1) };
        var w = axes[drop];
        var u = axes[(drop + 1) % 3];
        var v = Vec3.Cross(w, u);
        return (u, v, w);
    }

    internal static (Vec3 E1, Vec3 E2) Basis(Vec3 axis)
    {
        var helper = Math.Abs(axis.Z) < 0.9 ? new Vec3(0, 0, 1) : new Vec3(1, 0, 0);
        var e1 = Vec3.Cross(helper, axis).Normalized();
        var e2 = Vec3.Cross(axis, e1).Normalized();
        return (e1, e2);
    }

    internal static bool Solve(double[,] m, double[] b, out double[] x)
    {
        int n = b.Length;
        var a = (double[,])m.Clone();
        x = (double[])b.Clone();

        for (int col = 0; col < n; col++)
        {
            int pivot = col;
            for (int r = col + 1; r < n; r++)
                if (Math.Abs(a[r, col]) > Math.Abs(a[pivot, col])) pivot = r;
            if (Math.Abs(a[pivot, col]) < 1e-14) return false;

            if (pivot != col)
            {
                for (int c = 0; c < n; c++) (a[col, c], a[pivot, c]) = (a[pivot, c], a[col, c]);
                (x[col], x[pivot]) = (x[pivot], x[col]);
            }

            double diag = a[col, col];
            for (int r = col + 1; r < n; r++)
            {
                double f = a[r, col] / diag;
                if (f == 0) continue;
                for (int c = col; c < n; c++) a[r, c] -= f * a[col, c];
                x[r] -= f * x[col];
            }
        }

        for (int r = n - 1; r >= 0; r--)
        {
            double s = x[r];
            for (int c = r + 1; c < n; c++) s -= a[r, c] * x[c];
            x[r] = s / a[r, r];
        }
        return true;
    }
}
