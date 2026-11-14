using Meshsmith.Core.Fit;
using Meshsmith.Core.Geometry;
using Meshsmith.Core.Mesh;
using Xunit;

namespace Meshsmith.Core.Tests;

/// <summary>
/// A torus is fitted from the normals' LARGEST-variance direction (n.A = sin v, variance 1/2, while
/// every in-plane direction gives 1/4) and then from two signed distances along that axis; a sphere is
/// an algebraic least-squares fit. Both must be exact on clean bodies, must refuse the other kind, and
/// must refuse their own degenerate cases (near-sphere torus, 2-ring cylinder "sphere").
/// </summary>
public class TorusFitTests
{
    private const double R = 10, r = 3;

    [Fact]
    public void Fits_a_torus_major_and_minor_radius()
    {
        var mesh = Torus(R, r, 64, 32);
        var fit = Fitters.FitTorus(Points(mesh), Normals(mesh), PipelineTol(mesh), mesh.Diagonal);

        var t = Assert.IsType<TorusPrimitive>(fit);
        Assert.Equal(R, t.MajorRadius, 2);          // measured 9.997
        Assert.Equal(r, t.MinorRadius, 3);          // measured 2.9996
        Assert.Equal(1.0, Math.Abs(Vec3.Dot(t.Axis, new Vec3(0, 0, 1))), 6);
        Assert.True(t.FitMax <= PipelineTol(mesh));
    }

    [Fact]
    public void Fits_a_sphere_radius_and_centre()
    {
        var mesh = Sphere(7.5, 24, 48, cx: 1, cy: -2, cz: 3);
        var fit = Fitters.FitSphere(Points(mesh), PipelineTol(mesh), mesh.Diagonal);

        var s = Assert.IsType<SpherePrimitive>(fit);
        Assert.Equal(7.5, s.Radius, 6);
        Assert.Equal(1.0, s.Center.X, 6);
        Assert.Equal(-2.0, s.Center.Y, 6);
        Assert.Equal(3.0, s.Center.Z, 6);
        Assert.True(s.FitMax < 1e-9);
    }

    [Fact]
    public void Refuses_the_other_surface_kind()
    {
        var torus = Torus(R, r, 64, 32);
        Assert.Null(Fitters.FitSphere(Points(torus), PipelineTol(torus), torus.Diagonal));

        var sphere = Sphere(10, 32, 64);
        Assert.Null(Fitters.FitTorus(Points(sphere), Normals(sphere), PipelineTol(sphere), sphere.Diagonal));
    }

    /// <summary>A torus whose tube almost fills the ring is a sphere to any tolerance - refuse it.</summary>
    [Fact]
    public void Refuses_a_near_sphere_torus_and_a_horn_torus()
    {
        foreach (var (major, minor) in new[] { (10.0, 9.0), (10.0, 10.0), (10.0, 12.0), (0.9, 1.0) })
        {
            var mesh = Torus(major, minor, 64, 32);
            var fit = Fitters.FitTorus(Points(mesh), Normals(mesh), 0.05, mesh.Diagonal);
            Assert.Null(fit);
        }
    }

    /// <summary>
    /// Two circles lie exactly on a sphere, so a 2-ring cylinder passes an algebraic sphere fit at 1e-15.
    /// The latitude guard is the only thing that stops the classifier labelling every cylinder band a sphere.
    /// </summary>
    [Fact]
    public void Refuses_a_two_ring_cylinder_as_a_sphere()
    {
        var mesh = CylinderSide(10, 30, 48);
        var spheres = new[] { 1.0, 2.0, 4.0 }
            .Select(m => Fitters.FitSphere(Points(mesh), PipelineTol(mesh) * m, mesh.Diagonal))
            .ToArray();
        // raw: two circles ARE a sphere - documented trap, the guard does not live in the fitter
        Assert.All(spheres, s => Assert.NotNull(s));
        // through the classifier: the cylinder fit claims it first (plane/cylinder/cone before
        // sphere), so a cylinder band can never come back a sphere whatever the fitter would say
        var prim = PrimitiveClassifier.FitRegion(mesh, Enumerable.Range(0, mesh.TriangleCount).ToArray(),
                                                 PipelineTol(mesh), mesh.Diagonal);
        Assert.IsNotType<SpherePrimitive>(prim);
        Assert.IsType<CylinderPrimitive>(prim);
    }

    /// <summary>A torus fit needs the whole tube: a 48x24 torus fragments into patches no fit can take.</summary>
    [Fact]
    public void Refuses_a_torus_patch()
    {
        var mesh = Torus(R, r, 48, 24);
        // the first 8 quads only: 16 triangles, a patch, not a body
        var patch = Enumerable.Range(0, 16).ToArray();
        var (p, n) = PointsAndNormals(mesh, patch);
        Assert.Null(Fitters.FitTorus(p, n, PipelineTol(mesh), mesh.Diagonal));
    }

    // ---- helpers ----------------------------------------------------------------

    internal static double PipelineTol(MeshData mesh) => Math.Clamp(2e-4 * mesh.Diagonal, 1e-3, 0.5);

    internal static List<Vec3> Points(MeshData mesh) =>
        PointsAndNormals(mesh, Enumerable.Range(0, mesh.TriangleCount).ToArray()).Points;

    internal static List<Vec3> Normals(MeshData mesh) =>
        PointsAndNormals(mesh, Enumerable.Range(0, mesh.TriangleCount).ToArray()).Normals;

    /// <summary>Exactly PrimitiveClassifier.UniquePointsAndNormals: one point+normal per unique vertex.</summary>
    internal static (List<Vec3> Points, List<Vec3> Normals) PointsAndNormals(MeshData mesh, int[] triangles)
    {
        var slots = new Dictionary<int, int>();
        var points = new List<Vec3>();
        var sums = new List<Vec3>();
        foreach (var t in triangles)
        {
            var weighted = mesh.Normal(t) * mesh.Area(t);
            for (int k = 0; k < 3; k++)
            {
                int vi = mesh.Indices[t * 3 + k];
                if (!slots.TryGetValue(vi, out int slot))
                {
                    slot = points.Count; slots[vi] = slot;
                    points.Add(mesh.Vertices[vi]); sums.Add(Vec3.Zero);
                }
                sums[slot] += weighted;
            }
        }
        return (points, sums.Select(x => x.Normalized()).ToList());
    }

    internal static MeshData Torus(double major, double minor, int N, int M,
                                   double dx = 0, double dy = 0, double dz = 0)
    {
        var v = new List<Vec3>();
        var idx = new List<int>();
        for (int i = 0; i < N; i++)
        {
            double u = 2 * Math.PI * i / N;
            for (int j = 0; j < M; j++)
            {
                double w = 2 * Math.PI * j / M;
                v.Add(new Vec3(dx + (major + minor * Math.Cos(w)) * Math.Cos(u),
                               dy + (major + minor * Math.Cos(w)) * Math.Sin(u),
                               dz + minor * Math.Sin(w)));
            }
        }
        int Id(int i, int j) => (i % N) * M + (j % M);
        for (int i = 0; i < N; i++)
            for (int j = 0; j < M; j++)
            {
                idx.AddRange(new[] { Id(i, j), Id(i + 1, j), Id(i + 1, j + 1) });
                idx.AddRange(new[] { Id(i, j), Id(i + 1, j + 1), Id(i, j + 1) });
            }
        return new MeshData { Vertices = v.ToArray(), Indices = idx.ToArray() };
    }

    /// <summary>Two tori 40 mm apart - the phase's recognition fixture (see TorusRecognitionTests).</summary>
    internal static MeshData TwoTori(int N = 64, int M = 32)
    {
        const double gap = 40;
        var v = new List<Vec3>();
        var idx = new List<int>();
        for (int body = 0; body < 2; body++)
        {
            int b = v.Count;
            for (int i = 0; i < N; i++)
            {
                double u = 2 * Math.PI * i / N;
                for (int j = 0; j < M; j++)
                {
                    double w = 2 * Math.PI * j / M;
                    v.Add(new Vec3(body * gap + (R + r * Math.Cos(w)) * Math.Cos(u),
                                   (R + r * Math.Cos(w)) * Math.Sin(u), r * Math.Sin(w)));
                }
            }
            int Id(int i, int j) => b + (i % N) * M + (j % M);
            for (int i = 0; i < N; i++)
                for (int j = 0; j < M; j++)
                {
                    idx.AddRange(new[] { Id(i, j), Id(i + 1, j), Id(i + 1, j + 1) });
                    idx.AddRange(new[] { Id(i, j), Id(i + 1, j + 1), Id(i, j + 1) });
                }
        }
        return new MeshData { Vertices = v.ToArray(), Indices = idx.ToArray() };
    }

    internal static MeshData Sphere(double radius, int stacks, int slices,
                                    double cx = 0, double cy = 0, double cz = 0)
    {
        var v = new List<Vec3>();
        var idx = new List<int>();
        v.Add(new Vec3(cx, cy, cz + radius));
        for (int i = 1; i < stacks; i++)
        {
            double phi = Math.PI * i / stacks;
            double z = radius * Math.Cos(phi), rr = radius * Math.Sin(phi);
            for (int j = 0; j < slices; j++)
            {
                double th = 2 * Math.PI * j / slices;
                v.Add(new Vec3(cx + rr * Math.Cos(th), cy + rr * Math.Sin(th), cz + z));
            }
        }
        int south = v.Count;
        v.Add(new Vec3(cx, cy, cz - radius));

        int Ring(int i, int j) => 1 + (i - 1) * slices + (j % slices);
        for (int j = 0; j < slices; j++) idx.AddRange(new[] { 0, Ring(1, j), Ring(1, j + 1) });
        for (int i = 1; i < stacks - 1; i++)
            for (int j = 0; j < slices; j++)
            {
                idx.AddRange(new[] { Ring(i, j), Ring(i + 1, j), Ring(i + 1, j + 1) });
                idx.AddRange(new[] { Ring(i, j), Ring(i + 1, j + 1), Ring(i, j + 1) });
            }
        for (int j = 0; j < slices; j++)
            idx.AddRange(new[] { south, Ring(stacks - 1, j + 1), Ring(stacks - 1, j) });
        return new MeshData { Vertices = v.ToArray(), Indices = idx.ToArray() };
    }

    internal static MeshData CylinderSide(double radius, double height, int n)
    {
        var v = new List<Vec3>();
        var idx = new List<int>();
        for (int i = 0; i < n; i++)
        {
            double a = 2 * Math.PI * i / n;
            v.Add(new Vec3(radius * Math.Cos(a), radius * Math.Sin(a), 0));
            v.Add(new Vec3(radius * Math.Cos(a), radius * Math.Sin(a), height));
        }
        for (int i = 0; i < n; i++)
        {
            int a = 2 * i, b = 2 * ((i + 1) % n);
            idx.AddRange(new[] { a, b, b + 1 });
            idx.AddRange(new[] { a, b + 1, a + 1 });
        }
        return new MeshData { Vertices = v.ToArray(), Indices = idx.ToArray() };
    }
}
