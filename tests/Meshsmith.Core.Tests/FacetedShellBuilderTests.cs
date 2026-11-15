using Meshsmith.Core.Brep;
using Meshsmith.Core.Geometry;
using Meshsmith.Core.Mesh;
using Occt;
using Xunit;

namespace Meshsmith.Core.Tests;

/// <summary>
/// R5's premise, pinned down: a closed set of triangles becomes ONE solid by sharing the mesh's own
/// vertices and edges, with no sewing at all. The numbers come from the probe (run-S2.log), so a
/// regression in the sharing shows up as an edge count, not only as a timing.
/// </summary>
public class FacetedShellBuilderTests
{
    private static MeshData Tetrahedron() => new()
    {
        Vertices = new[]
        {
            new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(0, 1, 0), new Vec3(0, 0, 1)
        },
        Indices = new[] { 0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3 }
    };

    [Fact]
    public void A_closed_tetrahedron_becomes_one_solid_with_four_faces()
    {
        var mesh = Tetrahedron();

        var result = FacetedShellBuilder.Build(mesh, Enumerable.Range(0, 4).ToArray(), tolerance: 1e-9);

        Assert.True(result.IsValid);
        Assert.Equal(4, result.Faces);
        Assert.Equal(0, result.FreeEdges);
        Assert.Equal(1, result.Shells);
        Assert.Equal(1.0 / 6.0, result.Volume, 12);
    }

    /// <summary>
    /// THE phase: the shell must hold the mesh's 6 edges and 4 vertices, not 3 per triangle. The
    /// shape in the result is the only honest witness - a TopoDS_Edge cannot be a Dictionary key, so
    /// a census has to go through TopExp, which hashes by OCCT identity.
    /// </summary>
    [Fact]
    public void An_edge_is_shared_by_its_two_triangles()
    {
        var mesh = Tetrahedron();
        int topoEdges = MeshTopology.Build(mesh).EdgeCount;

        var result = FacetedShellBuilder.Build(mesh, Enumerable.Range(0, 4).ToArray(), tolerance: 1e-9,
                                               unify: false);

        Assert.Equal(6, topoEdges);
        Assert.Equal(6, SubShapes(result.Shape, TopAbs_ShapeEnum.TopAbs_EDGE));       // 3 * tris would be 12
        Assert.Equal(4, SubShapes(result.Shape, TopAbs_ShapeEnum.TopAbs_VERTEX));     // 3 * tris would be 12
    }

    [Fact]
    public void A_non_manifold_edge_still_produces_a_shape_without_throwing()
    {
        // Three triangles on edge 0-1. Nothing can close here, but a mesh in this state must produce
        // a shape and a verdict, not an exception - the pipeline's fallback still exports it.
        var mesh = new MeshData
        {
            Vertices = new[]
            {
                new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(0, 1, 0),
                new Vec3(0, -1, 0), new Vec3(0, 0, 1)
            },
            Indices = new[] { 0, 1, 2, 1, 0, 3, 0, 1, 4 }
        };

        var result = FacetedShellBuilder.Build(mesh, new[] { 0, 1, 2 }, tolerance: 1e-9);

        Assert.False(result.IsValid);
        Assert.Equal(6, result.FreeEdges);
        Assert.Equal(3, result.Faces);
    }

    [Fact]
    public void IsClosedSet_is_false_for_four_disjoint_closed_bodies()
    {
        // Every edge of this set is used exactly twice, so "closed" alone is not enough: it is four
        // separate cubes, and one shell around all four is ONE solid where the sewer makes four.
        // Measured on clamp.stl, which is exactly this shape: 4,444 faces / solids=4 today, and
        // 5,784 faces / solids=1 when the shell path took the whole 4-body set.
        var cube = Cube(10);
        var vertices = new List<Vec3>(cube.Vertices);
        var indices = new List<int>(cube.Indices);
        for (int body = 1; body < 4; body++)
        {
            int offset = vertices.Count;
            foreach (var v in cube.Vertices) vertices.Add(new Vec3(v.X + body * 30, v.Y, v.Z));
            foreach (int i in cube.Indices) indices.Add(i + offset);
        }
        var four = new MeshData { Vertices = vertices.ToArray(), Indices = indices.ToArray() };
        var all = Enumerable.Range(0, four.TriangleCount).ToArray();

        Assert.False(FacetedShellBuilder.IsClosedSet(four, all));

        // ...while each body on its own IS one closed body.
        Assert.True(FacetedShellBuilder.IsClosedSet(four, Enumerable.Range(0, cube.TriangleCount).ToArray()));
    }

    [Fact]
    public void IsClosedSet_is_true_for_a_closed_cube_and_false_for_a_patch()
    {
        var cube = Cube(10);
        Assert.True(FacetedShellBuilder.IsClosedSet(cube, Enumerable.Range(0, cube.TriangleCount).ToArray()));

        // The same cube with one triangle removed has a boundary loop: shelling it would produce a
        // valid-but-open shell instead of a solid, so the gate must send it back to the sewer.
        var holed = Enumerable.Range(0, cube.TriangleCount - 1).ToArray();
        Assert.False(FacetedShellBuilder.IsClosedSet(cube, holed));

        // Two triangles sharing one edge: 4 free edges in the probe's census.
        var patch = new MeshData
        {
            Vertices = new[]
            {
                new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(1, 1, 0), new Vec3(0, 1, 0)
            },
            Indices = new[] { 0, 1, 2, 0, 2, 3 }
        };
        Assert.False(FacetedShellBuilder.IsClosedSet(patch, new[] { 0, 1 }));
        Assert.False(FacetedShellBuilder.IsClosedSet(cube, Array.Empty<int>()));
    }

    // ---- helpers ----------------------------------------------------------------

    /// <summary>Number of DISTINCT sub-shapes, which is the only count that tells shared from loose.</summary>
    private static int SubShapes(TopoDS_Shape shape, TopAbs_ShapeEnum kind)
    {
        var map = new TopTools_IndexedDataMapOfShapeListOfShape();
        TopExp.MapShapesAndUniqueAncestors(shape, kind, TopAbs_ShapeEnum.TopAbs_SHAPE, out map);
        return map.Size;
    }

    /// <summary>Axis-aligned cube <paramref name="s"/>^3 as 12 triangles, outward winding, shared vertices.</summary>
    private static MeshData Cube(double s)
    {
        var vertices = new[]
        {
            new Vec3(0, 0, 0), new Vec3(s, 0, 0), new Vec3(s, s, 0), new Vec3(0, s, 0),
            new Vec3(0, 0, s), new Vec3(s, 0, s), new Vec3(s, s, s), new Vec3(0, s, s)
        };
        var indices = new List<int>();
        void Quad(int a, int b, int c, int d) { indices.AddRange(new[] { a, b, c, a, c, d }); }
        Quad(0, 3, 2, 1);   // z = 0
        Quad(4, 5, 6, 7);   // z = s
        Quad(0, 1, 5, 4);   // y = 0
        Quad(1, 2, 6, 5);   // x = s
        Quad(2, 3, 7, 6);   // y = s
        Quad(3, 0, 4, 7);   // x = 0
        return new MeshData { Vertices = vertices, Indices = indices.ToArray() };
    }
}
