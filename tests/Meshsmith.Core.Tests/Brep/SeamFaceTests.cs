using Meshsmith.Core.Brep;
using Meshsmith.Core.Fit;
using Meshsmith.Core.Geometry;
using Meshsmith.Core.Mesh;
using Occt;
using Xunit;

namespace Meshsmith.Core.Tests.Brep;

/// <summary>
/// T2: a face whose boundary touches a faceted region is built from the MESH polyline - one straight
/// edge per mesh edge, between the mesh's own vertices - because the faceted side is exactly that and
/// a fitted curve has no partner to sew to.
///
/// Measured constraint that shapes these tests: BRepBuilderAPI_MakeWire rejects a junction whose two
/// vertices are 1e-6 apart (BRepBuilderAPI_DisconnectedWire) and OCCT projects a vertex onto a pcurve
/// at 1e-7, while ATLAS's mesh vertices sit up to 1.6e-3 off their fitted surface. A wire therefore
/// cannot mix surface vertices with mesh vertices: a seam-bearing loop is built entirely on the mesh.
/// </summary>
public class SeamFaceTests
{
    /// <summary>
    /// A 10 x 5 rectangle on z = 0 whose bottom side is split into three collinear mesh edges:
    /// b0(0,0) b1(3,0) b2(7,0) b3(10,0), t0(0,5) t1(10,5). Four triangles, boundary loop
    /// [b0,b1,b2,b3,t1,t0].
    /// </summary>
    private static (MeshData Mesh, int[] Loop) RectangleWithSplitBottom()
    {
        var mesh = new MeshData
        {
            Vertices = new[]
            {
                new Vec3(0, 0, 0), new Vec3(3, 0, 0), new Vec3(7, 0, 0), new Vec3(10, 0, 0),
                new Vec3(0, 5, 0), new Vec3(10, 5, 0)
            },
            Indices = new[]
            {
                0, 1, 4,       // b0 b1 t0
                1, 2, 4,       // b1 b2 t0
                2, 3, 4,       // b2 b3 t0
                3, 5, 4        // b3 t1 t0
            }
        };
        var loop = Assert.Single(BoundaryLoops.Extract(mesh, MeshTopology.Build(mesh), new[] { 0, 1, 2, 3 }));
        return (mesh, loop);
    }

    private static Geom_Surface Plane() =>
        new Geom_Plane(new gp_Pln(new gp_Pnt(0, 0, 0), new gp_Dir(0, 0, 1)));

    private static readonly PlanePrimitive PlaneFit =
        new() { Point = new Vec3(0, 0, 0), Normal = new Vec3(0, 0, 1) };

    /// <summary>The bottom chain (both endpoints on y = 0) is the seam; the top and left are not.</summary>
    private static SeamLoop BottomIsSeam(MeshData mesh, int[] loop)
    {
        var mask = new bool[loop.Length];
        for (int i = 0; i < loop.Length; i++)
            mask[i] = mesh.Vertices[loop[i]].Y == 0 && mesh.Vertices[loop[(i + 1) % loop.Length]].Y == 0;
        return new SeamLoop(mesh, loop, mask);
    }

    private static List<TopoDS_Edge> Edges(TopoDS_Shape shape)
    {
        var edges = new List<TopoDS_Edge>();
        var ex = new TopExp_Explorer(shape, TopAbs_ShapeEnum.TopAbs_EDGE);
        while (ex.More) { edges.Add((TopoDS_Edge)ex.Current); ex.Next(); }
        return edges;
    }

    private static List<Vec3> Corners(TopoDS_Edge edge)
    {
        var points = new List<Vec3>();
        var ex = new TopExp_Explorer(edge, TopAbs_ShapeEnum.TopAbs_VERTEX);
        while (ex.More) { points.Add(Vec3.From(BRep_Tool.Pnt((TopoDS_Vertex)ex.Current))); ex.Next(); }
        return points;
    }

    private static double Distance(Vec3 a, Vec3 b) => (a - b).Length;

    [Fact]
    public void A_seam_edge_is_a_straight_line_between_mesh_vertices()
    {
        var (mesh, loop) = RectangleWithSplitBottom();
        var seam = BottomIsSeam(mesh, loop);
        Assert.Equal(3, seam.Seam.Count(b => b));

        var uv = UvProjector.Project(Plane(), loop.Select(i => mesh.Vertices[i]).ToArray());
        var face = Assert.Single(FaceBuilder.Build(Plane(), PlaneFit, new[] { uv }, uvTol: 1e-9,
            samplePoint: new Vec3(5, 2, 0), sampleNormal: new Vec3(0, 0, 1),
            seams: new SeamLoop?[] { seam }, seamFallbacks: out int fellBack));

        Assert.Equal(0, fellBack);
        Assert.Equal(50.0, face.Area, 6);

        // One straight edge per MESH edge on the seam: three edges, each a line between exactly the
        // mesh's own vertices. The fitted wire merges the collinear run into ONE edge, which is what
        // the faceted side cannot pair with.
        var edges = Edges(face);
        Assert.Equal(6, edges.Count);
        var bottom = new[] { (0, 1), (1, 2), (2, 3) };
        foreach (var (a, b) in bottom)
        {
            var edge = edges.Single(e =>
            {
                var corners = Corners(e);
                return corners.Count == 2 &&
                       (Distance(corners[0], mesh.Vertices[a]) < 1e-12 && Distance(corners[1], mesh.Vertices[b]) < 1e-12 ||
                        Distance(corners[0], mesh.Vertices[b]) < 1e-12 && Distance(corners[1], mesh.Vertices[a]) < 1e-12);
            });

            // A straight edge is stored as a degree-1 Bezier by this OCCT build (measured: MakeEdge(v1,v2)
            // gives Geom_BezierCurve, not Geom_Line), so the binding assertion is the GEOMETRY: the whole
            // curve, not just its midpoint, must lie on the chord between the two mesh vertices.
            var curve = BRep_Tool.Curve(edge, out double first, out double last);
            Assert.NotNull(curve);
            var a3 = mesh.Vertices[a];
            var b3 = mesh.Vertices[b];
            foreach (double t in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 })
            {
                var p = Vec3.From(curve.Value(first + (last - first) * t));
                // distance from the point to the chord's line
                var d = (b3 - a3);
                double len = d.Length;
                double off = ((p - a3) - d * (Vec3.Dot(p - a3, d) / (len * len))).Length;
                Assert.True(off < 1e-12,
                    $"the seam edge is not the straight chord between its mesh vertices ({off:E3} at t={t})");
            }
        }
    }

    [Fact]
    public void A_seam_does_not_change_a_face_without_facets()
    {
        var (mesh, loop) = RectangleWithSplitBottom();
        var uv = UvProjector.Project(Plane(), loop.Select(i => mesh.Vertices[i]).ToArray());
        var noSeam = new SeamLoop(mesh, loop, new bool[loop.Length]);

        var fitted = Assert.Single(FaceBuilder.Build(Plane(), PlaneFit, new[] { uv }, 1e-9,
            new Vec3(5, 2, 0), new Vec3(0, 0, 1), seams: null, seamFallbacks: out _));
        var allFalse = Assert.Single(FaceBuilder.Build(Plane(), PlaneFit, new[] { uv }, 1e-9,
            new Vec3(5, 2, 0), new Vec3(0, 0, 1), seams: new SeamLoop?[] { noSeam }, seamFallbacks: out int fellBack));

        Assert.Equal(0, fellBack);
        Assert.Equal(Edges(fitted).Count, Edges(allFalse).Count);
        Assert.Equal(fitted.Area, allFalse.Area, 12);
    }

    [Fact]
    public void A_seam_face_stays_valid_or_keeps_its_fitted_wire()
    {
        // The controller-mandated check: a straight chord between two mesh vertices carries no pcurve
        // for the analytic surface, so OCCT may reject the face. It must then keep the fitted wire -
        // per FACE, never per part - and the fallback rate must stay under 10 % of seam faces.
        var (mesh, loop) = RectangleWithSplitBottom();
        var seam = BottomIsSeam(mesh, loop);
        var uv = UvProjector.Project(Plane(), loop.Select(i => mesh.Vertices[i]).ToArray());

        var face = Assert.Single(FaceBuilder.Build(Plane(), PlaneFit, new[] { uv }, 1e-9,
            new Vec3(5, 2, 0), new Vec3(0, 0, 1),
            seams: new SeamLoop?[] { seam }, seamFallbacks: out int fellBack));

        var fitted = Assert.Single(FaceBuilder.Build(Plane(), PlaneFit, new[] { uv }, 1e-9,
            new Vec3(5, 2, 0), new Vec3(0, 0, 1), seams: null, seamFallbacks: out _));

        Assert.True(fellBack <= 0.10 * 1, $"{fellBack} of 1 seam faces fell back (over the 10 % budget)");

        if (fellBack == 0)
        {
            Assert.True(new BRepCheck_Analyzer(face).IsValid(), "a kept seam face must pass BRepCheck on its own");
            Assert.Equal(6, Edges(face).Count);        // the seam survived: one edge per mesh edge
        }
        else
        {
            // The degradation is the fitted face, unchanged - not a broken one.
            Assert.Equal(Edges(fitted).Count, Edges(face).Count);
            Assert.Equal(fitted.Area, face.Area, 12);
        }
    }
}
