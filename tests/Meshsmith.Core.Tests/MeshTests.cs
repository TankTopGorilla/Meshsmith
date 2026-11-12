using Meshsmith.Core.Geometry;
using Meshsmith.Core.Mesh;
using Xunit;

namespace Meshsmith.Core.Tests;

public class WelderTests
{
    [Fact]
    public void Welds_cube_to_eight_vertices()
    {
        var soup = StlReader.Read(StlFixtures.WriteCube(10));
        var mesh = MeshWelder.Weld(soup, tolerance: 1e-6);
        Assert.Equal(8, mesh.Vertices.Length);
        Assert.Equal(12, mesh.TriangleCount);
        Assert.Equal(1000.0, mesh.SignedVolume(), 6);
    }

    [Fact]
    public void Welds_cylinder_to_two_rings_plus_centres()
    {
        var soup = StlReader.Read(StlFixtures.WriteCylinder(20, 30, 64));
        var mesh = MeshWelder.Weld(soup, tolerance: 1e-6);
        // 64 + 64 ring vertices + 2 centres
        Assert.Equal(130, mesh.Vertices.Length);
    }
}

public class TopologyTests
{
    [Fact]
    public void Cube_has_eighteen_manifold_edges()
    {
        var mesh = MeshWelder.Weld(StlReader.Read(StlFixtures.WriteCube(10)), 1e-6);
        var topo = MeshTopology.Build(mesh);
        Assert.Equal(18, topo.EdgeCount);
        Assert.DoesNotContain(true, topo.NonManifold);
        Assert.All(Enumerable.Range(0, mesh.TriangleCount * 3),
            i => Assert.NotEqual(-1, topo.Neighbor[i]));
    }
}

public class GridTests
{
    [Fact]
    public void Grid_finds_nearest_triangle()
    {
        var mesh = MeshWelder.Weld(StlReader.Read(StlFixtures.WriteCube(10)), 1e-6);
        var grid = new TriangleGrid(mesh);
        Assert.Equal(0.0, grid.DistanceToSurface(new Vec3(5, 5, 0)), 9);       // on the bottom face
        Assert.Equal(2.0, grid.DistanceToSurface(new Vec3(5, 5, 12)), 6);      // 2 mm above the top
    }
}
