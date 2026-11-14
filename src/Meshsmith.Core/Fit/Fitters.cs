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

    /// <summary>
    /// Torus: axis first, then the two radii.
    ///
    /// The axis is the LARGEST-eigenvector direction of sum(n n^T), not the smallest: a torus's
    /// normals satisfy n.A = sin v (variance 1/2) while every direction in the tube's own plane gives
    /// variance 1/4, so the axis is the direction the normals agree on MOST. This is the opposite of
    /// a cylinder and it is why FitCylinder must not be reused here.
    ///
    /// With the axis fixed, p.A = d + r (n.A) is linear in the two signed distances (d = the axis
    /// offset, r = the minor radius). Then q = p - r n lies exactly on the tube-centre circle, so a
    /// Kasa circle fit in the plane perpendicular to A gives the centre and R.
    /// </summary>
    public static TorusPrimitive? FitTorus(IReadOnlyList<Vec3> pts, IReadOnlyList<Vec3> normals,
                                           double tol, double diag)
    {
        // A torus needs the whole tube: measured, a 48x24 torus patch never fills one (30 vertices is
        // the point where the Kasa circle below becomes determined, 16 is not).
        if (pts.Count < 32) return null;
        if (!TryMaxDirection(normals, out var axis)) return null;

        double s11 = 0, s12 = 0, s22 = 0, t1 = 0, t2 = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            double nA = Vec3.Dot(normals[i], axis), pA = Vec3.Dot(pts[i], axis);
            s11 += nA * nA; s12 += nA; s22 += 1;
            t1 += nA * pA; t2 += pA;
        }
        if (!Solve(new[,] { { s11, s12 }, { s12, s22 } }, new[] { t1, t2 }, out var xr)) return null;
        double r = xr[0], d = xr[1];
        if (r < 1e-6 || r > diag) return null;

        var (e1, e2) = Basis(axis);
        var q = new (double X, double Y)[pts.Count];
        for (int i = 0; i < pts.Count; i++)
        {
            var onCircle = pts[i] - normals[i] * r;
            q[i] = (Vec3.Dot(onCircle, e1), Vec3.Dot(onCircle, e2));
        }
        if (!TryCircle(q, out double cx, out double cy, out double major)) return null;
        if (major < 1e-6 || major > 10 * diag) return null;
        // r >= R is a spindle/horn torus: its tube crosses the axis, so it is not the surface this
        // primitive describes. Measured: every (R=10, r>=9) and (R=0.9, r=1) body is refused here.
        if (r >= major) return null;

        var centre = e1 * cx + e2 * cy + axis * d;

        double max = 0, sum = 0;
        foreach (var p in pts)
        {
            var dd = p - centre;
            double z = Vec3.Dot(dd, axis);
            double rho = (dd - axis * z).Length;
            double err = Math.Abs(Math.Sqrt((rho - major) * (rho - major) + z * z) - r);
            max = Math.Max(max, err); sum += err;
            if (max > tol) return null;
        }

        return new TorusPrimitive
        {
            PointOnAxis = centre, Axis = axis, MajorRadius = major, MinorRadius = r,
            FitMax = max, FitMean = sum / pts.Count
        };
    }

    /// <summary>
    /// Sphere: algebraic least squares. |p - c|^2 = R^2 expands to x^2+y^2+z^2 = 2 c.p + (R^2 - |c|^2),
    /// which is linear in (cx, cy, cz, k), so one 4x4 solve gives the centre and R.
    ///
    /// This fit has NO discriminating power of its own: two concentric circles are exactly a sphere
    /// (measured: a 2-ring cylinder -> residual 3.6e-15; a cone -> 7.1e-15), and so is a torus band.
    /// Callers MUST apply the latitude guard (PrimitiveClassifier.FitRegion does); the residual test
    /// below only rejects a body that is not co-radial at all.
    /// </summary>
    public static SpherePrimitive? FitSphere(IReadOnlyList<Vec3> pts, double tol, double diag)
    {
        if (pts.Count < 8) return null;

        double a11 = 0, a12 = 0, a13 = 0, a14 = 0, a22 = 0, a23 = 0, a24 = 0, a33 = 0, a34 = 0, a44 = 0;
        double b1 = 0, b2 = 0, b3 = 0, b4 = 0;
        foreach (var p in pts)
        {
            double r0 = 2 * p.X, r1 = 2 * p.Y, r2 = 2 * p.Z, r3 = 1;
            double rhs = p.X * p.X + p.Y * p.Y + p.Z * p.Z;
            a11 += r0 * r0; a12 += r0 * r1; a13 += r0 * r2; a14 += r0 * r3;
            a22 += r1 * r1; a23 += r1 * r2; a24 += r1 * r3;
            a33 += r2 * r2; a34 += r2 * r3;
            a44 += r3 * r3;
            b1 += r0 * rhs; b2 += r1 * rhs; b3 += r2 * rhs; b4 += r3 * rhs;
        }
        double[,] m = { { a11, a12, a13, a14 }, { a12, a22, a23, a24 }, { a13, a23, a33, a34 }, { a14, a24, a34, a44 } };
        if (!Solve(m, new[] { b1, b2, b3, b4 }, out var x)) return null;

        var centre = new Vec3(x[0], x[1], x[2]);
        double radiusSq = x[3] + Vec3.Dot(centre, centre);
        if (radiusSq <= 0) return null;
        double radius = Math.Sqrt(radiusSq);
        if (radius < 1e-6 || radius > 10 * diag) return null;

        double max = 0, sum = 0;
        foreach (var p in pts)
        {
            double err = Math.Abs((p - centre).Length - radius);
            max = Math.Max(max, err); sum += err;
            if (max > tol) return null;
        }
        return new SpherePrimitive
        {
            Center = centre, Radius = radius, FitMax = max, FitMean = sum / pts.Count
        };
    }

    /// <summary>
    /// The largest-eigenvector direction of sum(n n^T), by power iteration seeded on the coordinate
    /// axis with the most normal energy (so it cannot start orthogonal to the answer).
    /// </summary>
    internal static bool TryMaxDirection(IReadOnlyList<Vec3> normals, out Vec3 dir)
    {
        dir = Vec3.Zero;
        if (normals.Count == 0) return false;

        double ex = 0, ey = 0, ez = 0;
        foreach (var n in normals) { ex += n.X * n.X; ey += n.Y * n.Y; ez += n.Z * n.Z; }
        var v = ex >= ey && ex >= ez ? new Vec3(1, 0, 0) : (ey >= ez ? new Vec3(0, 1, 0) : new Vec3(0, 0, 1));

        for (int it = 0; it < 200; it++)
        {
            var w = Vec3.Zero;
            foreach (var n in normals) w += n * Vec3.Dot(n, v);
            if (w.Length < 1e-18) return false;
            w = w.Normalized();
            bool done = (w - v).Length < 1e-12;
            v = w;
            if (done) break;
        }
        dir = v;
        return true;
    }

    /// <summary>Kasa algebraic circle fit; false when the points are collinear or the matrix is singular.</summary>
    internal static bool TryCircle((double X, double Y)[] pts, out double cx, out double cy, out double radius)
    {
        cx = cy = radius = 0;
        if (pts.Length < 3) return false;

        double suu = 0, suv = 0, svv = 0, su = 0, sv = 0, s1 = 0, suw = 0, svw = 0, sw = 0;
        foreach (var p in pts)
        {
            double w = p.X * p.X + p.Y * p.Y;
            suu += p.X * p.X; suv += p.X * p.Y; svv += p.Y * p.Y;
            su += p.X; sv += p.Y; s1 += 1;
            suw += p.X * w; svw += p.Y * w; sw += w;
        }
        double[,] m = { { suu, suv, su }, { suv, svv, sv }, { su, sv, s1 } };
        if (!Solve(m, new[] { -suw, -svw, -sw }, out var x)) return false;

        cx = -x[0] / 2; cy = -x[1] / 2;
        double r2 = cx * cx + cy * cy - x[2];
        if (r2 <= 0) return false;
        radius = Math.Sqrt(r2);
        return true;
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
