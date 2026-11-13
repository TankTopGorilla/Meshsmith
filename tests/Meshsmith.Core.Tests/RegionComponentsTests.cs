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
