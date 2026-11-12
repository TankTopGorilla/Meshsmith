using Meshsmith.Core.Geometry;
using Meshsmith.Core.Mesh;
using Xunit;

namespace Meshsmith.Core.Tests;

public class MeshHealthTests
{
    private static MeshData Weld(IEnumerable<(Vec3 A, Vec3 B, Vec3 C)> triangles)
    {
        var points = triangles.SelectMany(t => new[] { t.A, t.B, t.C }).ToArray();
        return MeshWelder.Weld(new TriangleSoup { Points = points }, 1e-6);
    }

    [Fact]
    public void A_closed_cube_has_no_open_edges_and_one_body()
    {
        var mesh = Weld(StlFixtures.Cube(10));
        var health = MeshHealth.Measure(mesh, MeshTopology.Build(mesh));

        Assert.Equal(0, health.OpenEdges);
        Assert.Equal(0, health.NonManifoldEdges);
        Assert.Equal(1, health.Bodies);
        Assert.True(health.IsWatertight);
    }

    [Fact]
    public void Two_triangles_in_a_strip_report_four_open_edges()
    {
        var a = new Vec3(0, 0, 0);
        var b = new Vec3(10, 0, 0);
        var c = new Vec3(10, 10, 0);
        var d = new Vec3(0, 10, 0);

        var mesh = Weld(new[] { (a, b, c), (a, c, d) });
        var health = MeshHealth.Measure(mesh, MeshTopology.Build(mesh));

        Assert.Equal(4, health.OpenEdges);
        Assert.Equal(1, health.Bodies);
        Assert.False(health.IsWatertight);
    }

    [Fact]
    public void Two_boxes_far_apart_are_two_bodies_but_still_watertight()
    {
        var apart = StlFixtures.Cube(10).Select(t => (
            new Vec3(t.A.X + 100, t.A.Y, t.A.Z),
            new Vec3(t.B.X + 100, t.B.Y, t.B.Z),
            new Vec3(t.C.X + 100, t.C.Y, t.C.Z)));
        var mesh = Weld(StlFixtures.Cube(10).Concat(apart));
        var health = MeshHealth.Measure(mesh, MeshTopology.Build(mesh));

        Assert.Equal(2, health.Bodies);
        Assert.Equal(0, health.OpenEdges);
        Assert.True(health.IsWatertight);
    }
}
