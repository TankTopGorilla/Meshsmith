using Meshsmith.Core.Fit;
using Meshsmith.Core.Geometry;
using Meshsmith.Core.Mesh;
using Meshsmith.Core.Pipeline;
using Xunit;

namespace Meshsmith.Core.Tests;

public class RegionComponentsTests
{
    [Fact]
    public void Two_bodies_stay_two_components()
    {
        var (mesh, topo) = TwoCubes();
        var regions = new List<ClassifiedRegion>
        {
            Region(Enumerable.Range(0, 12).ToArray()),
            Region(Enumerable.Range(12, 12).ToArray())
        };

        var components = RegionComponents.Group(mesh, topo, regions);

        Assert.Equal(2, components.Count);
        Assert.Equal(24, components.Sum(c => c.Sum(r => regions[r].Triangles.Length)));
    }

    [Fact]
    public void Adjacent_regions_sharing_a_boundary_edge_are_one_component()
    {
        var (mesh, topo) = TwoQuads();
        var regions = new List<ClassifiedRegion>
        {
            Region(new[] { 0, 1 }),
            Region(new[] { 2, 3 })
        };

        Assert.Single(RegionComponents.Group(mesh, topo, regions));
    }

    /// <summary>
    /// The phase's objective in one assertion: on a two-body part the classifier's own output must
    /// group into two components, or the pipeline's per-body degradation has nothing to degrade.
    /// Two tori are two bodies, and their unrecognised triangles are the only thing joining them.
    /// </summary>
    [Fact]
    public void Classifier_output_of_a_two_body_part_groups_into_two_components()
    {
        var mesh = TwoTori();
        var topo = MeshTopology.Build(mesh);
        var regions = PrimitiveClassifier.Classify(mesh,
            new PrimitiveClassifier.Options(Math.Clamp(2e-4 * mesh.Diagonal, 1e-3, 0.5), 12, 3));

        var components = RegionComponents.Group(mesh, topo, regions);

        Assert.Equal(2, components.Count);
        Assert.All(components, c => Assert.Single(
            c.SelectMany(r => regions[r].Triangles).Select(t => t < 576 ? 0 : 1).Distinct()));
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

    private static ClassifiedRegion Region(int[] triangles) =>
        new(triangles, new FacetedPrimitive { Triangles = triangles.Length }, new Vec3(0, 0, 1), triangles.Length);

    /// <summary>Two 1 mm cubes 5 mm apart: 16 vertices, 24 triangles, vertex-disjoint.</summary>
    private static (MeshData Mesh, MeshTopology Topology) TwoCubes()
    {
        var vertices = new List<Vec3>();
        var indices = new List<int>();
        int[] quads = { 0, 3, 2, 1, 4, 5, 6, 7, 0, 1, 5, 4, 1, 2, 6, 5, 2, 3, 7, 6, 3, 0, 4, 7 };

        foreach (double offset in new[] { 0.0, 5.0 })
        {
            int b = vertices.Count;
            for (int i = 0; i < 8; i++)
                vertices.Add(new Vec3(offset + (i & 1), (i >> 1) & 1, (i >> 2) & 1));

            for (int q = 0; q < quads.Length; q += 4)
            {
                indices.AddRange(new[] { b + quads[q], b + quads[q + 1], b + quads[q + 2] });
                indices.AddRange(new[] { b + quads[q], b + quads[q + 2], b + quads[q + 3] });
            }
        }

        var mesh = new MeshData { Vertices = vertices.ToArray(), Indices = indices.ToArray() };
        return (mesh, MeshTopology.Build(mesh));
    }

    /// <summary>Two unit quads side by side, each split into two triangles; they share the edge (1,2).</summary>
    private static (MeshData Mesh, MeshTopology Topology) TwoQuads()
    {
        var mesh = new MeshData
        {
            Vertices = new[]
            {
                new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(1, 1, 0), new Vec3(0, 1, 0),
                new Vec3(2, 0, 0), new Vec3(2, 1, 0)
            },
            Indices = new[] { 0, 1, 2, 0, 2, 3, 1, 4, 5, 1, 5, 2 }
        };
        return (mesh, MeshTopology.Build(mesh));
    }
}
