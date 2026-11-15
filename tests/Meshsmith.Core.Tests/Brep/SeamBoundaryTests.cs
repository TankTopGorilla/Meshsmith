using Meshsmith.Core.Brep;
using Meshsmith.Core.Geometry;
using Meshsmith.Core.Mesh;
using Xunit;

namespace Meshsmith.Core.Tests.Brep;

/// <summary>
/// The seam predicate, as a pure function. E1's diagnosis: an analytic face's boundary that borders a
/// FACETED region has no partner face in the analytic-only per-body list, so the seam is open by
/// construction (probe: 8 of ATLAS component 4's 13 free edges, all within 1 um of a mesh edge).
/// The fix is to build those boundary pieces from the mesh's own vertices, so the predicate has to be
/// per MESH EDGE, not per loop and not per region.
/// </summary>
public class SeamBoundaryTests
{
    /// <summary>
    /// A closed octahedron: four triangles meet at the top vertex, four at the bottom. Region 0 is the
    /// two top triangles 0-1-4 and 1-2-4; the two triangles across its boundary are 2-3-4 (faceted,
    /// region 1) on edge (2,4) and 3-0-4 (faceted) on edge (4,0), while the equator edges (0,1) and
    /// (1,2) are shared with the analytic bottom region 2. Closed, so an "open boundary" cannot muddy
    /// the assertion - that case has its own test.
    /// </summary>
    private static (MeshData Mesh, MeshTopology Topo, int[] Loop, int[] TriangleRegion) Octahedron()
    {
        var mesh = new MeshData
        {
            Vertices = new[]
            {
                new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(1, 1, 0), new Vec3(0, 1, 0),
                new Vec3(0.5, 0.5, 0.5), new Vec3(0.5, 0.5, -0.5)
            },
            Indices = new[]
            {
                0, 1, 4,   1, 2, 4,   2, 3, 4,   3, 0, 4,
                1, 0, 5,   2, 1, 5,   3, 2, 5,   0, 3, 5
            }
        };
        var topo = MeshTopology.Build(mesh);
        var loop = BoundaryLoops.Extract(mesh, topo, new[] { 0, 1 }).Single();
        var triangleRegion = new[] { 0, 0, 1, 1, 2, 2, 2, 2 };
        return (mesh, topo, loop, triangleRegion);
    }

    private static HashSet<(int, int)> SeamEdges(int[] loop, bool[] mark)
    {
        var set = new HashSet<(int, int)>();
        for (int i = 0; i < loop.Length; i++)
            if (mark[i]) set.Add((loop[i], loop[(i + 1) % loop.Length]));
        return set;
    }

    [Fact]
    public void A_loop_point_bordering_a_faceted_region_is_marked_as_a_seam()
    {
        var (mesh, topo, loop, triangleRegion) = Octahedron();

        var mark = SeamBoundary.Mark(mesh, topo, loop,
            regionIsFaceted: r => r == 1,
            triangleRegion: t => triangleRegion[t]);

        // The load-bearing assertion: exactly the two boundary edges that face the FACETED triangle
        // are seams. Not the whole loop (the equator borders an analytic region and must stay fitted),
        // and not per region (the region has both kinds of neighbour).
        Assert.Equal(new HashSet<(int, int)> { (2, 4), (4, 0) }, SeamEdges(loop, mark));
    }

    [Fact]
    public void A_loop_point_bordering_another_analytic_region_is_not_a_seam()
    {
        var (mesh, topo, loop, triangleRegion) = Octahedron();

        // Same mesh, every region analytic: this is the pure-analytic path, which must stay
        // byte-identical to the fitted-curve wire it has always used.
        var mark = SeamBoundary.Mark(mesh, topo, loop,
            regionIsFaceted: _ => false,
            triangleRegion: t => triangleRegion[t]);

        Assert.All(mark, Assert.False);
    }

    [Fact]
    public void An_open_mesh_boundary_edge_is_a_seam()
    {
        // One triangle: no neighbour across any of its three edges, so nothing can ever pair with
        // them. Part 1's 23 open mesh edges must be an explicit seam, not an accident of the walk.
        var mesh = new MeshData
        {
            Vertices = new[] { new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(0, 1, 0) },
            Indices = new[] { 0, 1, 2 }
        };
        var topo = MeshTopology.Build(mesh);
        var loop = Assert.Single(BoundaryLoops.Extract(mesh, topo, new[] { 0 }));

        var mark = SeamBoundary.Mark(mesh, topo, loop,
            regionIsFaceted: _ => false,
            triangleRegion: _ => 0);

        Assert.Equal(3, mark.Length);
        Assert.All(mark, Assert.True);
    }
}
