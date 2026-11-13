using Meshsmith.Core.Fit;
using Meshsmith.Core.Geometry;
using Meshsmith.Core.Mesh;
using Meshsmith.Core.Pipeline;
using Xunit;

namespace Meshsmith.Core.Tests;

/// <summary>
/// The classifier hands the pipeline regions, and the pipeline turns one region into one face and
/// groups regions into bodies. Both need a region to be ONE connected patch of ONE body: a region that
/// spans two bodies makes RegionComponents.Group report a single component for the whole part, and the
/// per-body degradation then never engages (measured on clamp, IKI P1 and IKI Part 1).
/// </summary>
public class PrimitiveClassifierSplitTests
{
    private const int TrianglesPerTorus = 24 * 12 * 2;   // 576

    /// <summary>
    /// A torus fits no plane, cylinder or cone, so all of its triangles end up in the leftover
    /// region. Two tori 40 mm apart are two bodies, and today ONE FacetedPrimitive holds both.
    /// </summary>
    [Fact]
    public void Two_disconnected_leftover_bodies_yield_separate_faceted_regions()
    {
        var mesh = TwoTori();
        var regions = PrimitiveClassifier.Classify(mesh, Options(mesh));

        var faceted = regions.Where(r => r.Primitive is FacetedPrimitive).ToList();

        Assert.NotEmpty(faceted);
        // No leftover region may hold triangles of both bodies - not even one triangle of the other.
        Assert.All(faceted, r => Assert.Single(r.Triangles.Select(BodyOf).Distinct()));
        // ...and both bodies' unrecognised triangles must be represented, each in its own region(s).
        Assert.Equal(2, faceted.Select(r => BodyOf(r.Triangles[0])).Distinct().Count());
    }

    /// <summary>Guard: a part with nothing unrecognised must not grow a faceted region.</summary>
    [Fact]
    public void A_fully_analytic_part_yields_no_faceted_region()
    {
        var mesh = OneCube();
        var regions = PrimitiveClassifier.Classify(mesh, Options(mesh));

        Assert.DoesNotContain(regions, r => r.Primitive is FacetedPrimitive);
        Assert.Equal(12, regions.Sum(r => r.Triangles.Length));
    }

    /// <summary>
    /// Two cubes sitting on the same z = 0 plane: MergeSameSurface joins their bottom faces into ONE
    /// plane region, and that region is what welds the two bodies together in RegionComponents.Group.
    /// Measured today: 8 regions, 4 of them spanning both bodies, Group = 1 component.
    /// </summary>
    [Fact]
    public void Coplanar_faces_of_two_bodies_are_not_merged_into_one_region()
    {
        var (mesh, _) = TwoCubes(gap: 20);
        var topo = MeshTopology.Build(mesh);
        var regions = PrimitiveClassifier.Classify(mesh, Options(mesh));

        // Cube 0 owns triangles 0..11, cube 1 owns 12..23.
        Assert.DoesNotContain(regions, r => r.Triangles.Select(t => t < 12 ? 0 : 1).Distinct().Count() > 1);
        Assert.Equal(2, RegionComponents.Group(mesh, topo, regions).Count);
    }

    private static int BodyOf(int triangle) => triangle < TrianglesPerTorus ? 0 : 1;

    /// <summary>The pipeline's own tolerance, so the fixture is classified the way a real part is.</summary>
    private static PrimitiveClassifier.Options Options(MeshData mesh) =>
        new(Math.Clamp(2e-4 * mesh.Diagonal, 1e-3, 0.5), 12, 3);

    private static MeshData OneCube()
    {
        var (mesh, _) = TwoCubes(0, offset: false);
        return mesh;
    }

    /// <summary>Two 1 mm cubes; <paramref name="gap"/> apart on x, both sitting on z = 0.</summary>
    private static (MeshData Mesh, int[] BodyOfTri) TwoCubes(double gap, bool offset = true)
    {
        var vertices = new List<Vec3>();
        var indices = new List<int>();
        var bodies = new List<int>();
        int[] quads = { 0, 3, 2, 1, 4, 5, 6, 7, 0, 1, 5, 4, 1, 2, 6, 5, 2, 3, 7, 6, 3, 0, 4, 7 };

        for (int body = 0; body < (offset ? 2 : 1); body++)
        {
            int b = vertices.Count;
            double dx = offset ? body * gap : 0;
            for (int i = 0; i < 8; i++)
                vertices.Add(new Vec3(dx + (i & 1), (i >> 1) & 1, (i >> 2) & 1));

            for (int q = 0; q < quads.Length; q += 4)
            {
                indices.AddRange(new[] { b + quads[q], b + quads[q + 1], b + quads[q + 2] });
                indices.AddRange(new[] { b + quads[q], b + quads[q + 2], b + quads[q + 3] });
                bodies.Add(body);
                bodies.Add(body);
            }
        }

        return (new MeshData { Vertices = vertices.ToArray(), Indices = indices.ToArray() }, bodies.ToArray());
    }

    /// <summary>Two tori, R = 10, r = 3, 24 x 12 segments, 40 mm apart: 1,152 triangles, two bodies.</summary>
    private static MeshData TwoTori()
    {
        const int N = 24, M = 12;
        const double R = 10, r = 3, gap = 40;

        var vertices = new List<Vec3>();
        var indices = new List<int>();

        for (int body = 0; body < 2; body++)
        {
            int b = vertices.Count;
            double dx = body * gap;
            for (int i = 0; i < N; i++)
            {
                double u = 2 * Math.PI * i / N;
                for (int j = 0; j < M; j++)
                {
                    double v = 2 * Math.PI * j / M;
                    vertices.Add(new Vec3(dx + (R + r * Math.Cos(v)) * Math.Cos(u),
                                          (R + r * Math.Cos(v)) * Math.Sin(u),
                                          r * Math.Sin(v)));
                }
            }

            int Id(int i, int j) => b + (i % N) * M + (j % M);
            for (int i = 0; i < N; i++)
                for (int j = 0; j < M; j++)
                {
                    indices.AddRange(new[] { Id(i, j), Id(i + 1, j), Id(i + 1, j + 1) });
                    indices.AddRange(new[] { Id(i, j), Id(i + 1, j + 1), Id(i, j + 1) });
                }
        }

        return new MeshData { Vertices = vertices.ToArray(), Indices = indices.ToArray() };
    }
}
