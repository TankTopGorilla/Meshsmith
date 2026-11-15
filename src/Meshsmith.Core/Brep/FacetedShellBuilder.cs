using Meshsmith.Core.Geometry;
using Meshsmith.Core.Mesh;
using Occt;

namespace Meshsmith.Core.Brep;

/// <summary>
/// Builds one solid out of a CLOSED set of mesh triangles by reusing the mesh's own adjacency: one
/// <c>TopoDS_Vertex</c> per used mesh vertex, one <c>TopoDS_Edge</c> per undirected mesh edge, one
/// three-edge wire per triangle. Nothing is stitched, so there is no sewing tolerance and no ladder.
///
/// Measured at 60,000 triangles (R5 probe, run-D2.log): 12.157 s to build and 2.862 s to unify,
/// against 40.8 s for one clean sew and 281.6 s for a failing four-rung ladder - with the same
/// output as today (60,000 faces unify to 59,849, volume 9575.456, valid).
///
/// An edge traversed in reverse is the canonical edge <c>Reversed</c>. That single detail is what
/// makes two triangles with opposite winding share one edge and lets the shell close; without it the
/// result is 3·tris loose edges and no body at all.
/// </summary>
public static class FacetedShellBuilder
{
    /// <summary>
    /// How many shells have been built in this process. A diagnostic counter, tests only: the R5
    /// routing tests use it to observe what the pipeline actually did. Public because this assembly
    /// has no <c>InternalsVisibleTo</c>; nothing in the product reads it.
    /// </summary>
    public static int BuildCallCount;

    /// <summary>
    /// The value of <see cref="SolidBuilder.SewCallCount"/> the last time a shell was built, so a
    /// test can prove the shell was the FINAL body: when it is written, no sewer has touched the
    /// part since. Also tests only.
    /// </summary>
    public static int SewCallsWhenBuilt;

    /// <summary>
    /// Builds one solid from a closed set of mesh triangles, sharing vertices and edges.
    /// </summary>
    /// <param name="tolerance">
    /// The pipeline's sewing tolerance, accepted to keep this call site the same shape as
    /// <see cref="SolidBuilder.SewRobust"/>. It decides nothing here: the triangles already share
    /// their vertices exactly, so there is no crack to close.
    /// </param>
    /// <param name="unify">
    /// Merge coplanar neighbours (the same merge the sew path applies). Off for closed torus/sphere
    /// bodies, whose merged doubly-periodic face writes an empty STEP (see <see cref="SolidBuilder"/>).
    /// </param>
    public static SolidResult Build(MeshData mesh, int[] triangles, double tolerance, bool unify = true)
    {
        _ = tolerance;
        if (triangles.Length == 0) throw new InvalidOperationException("no triangles to build a shell from");
        BuildCallCount++;
        SewCallsWhenBuilt = SolidBuilder.SewCallCount;

        // Lazy, so a vertex no triangle of this set references never enters the shell.
        var vertexOf = new TopoDS_Vertex[mesh.Vertices.Length];
        TopoDS_Vertex VertexOf(int i) =>
            vertexOf[i] ??= new BRepBuilderAPI_MakeVertex(mesh.Vertices[i].ToPnt()).Vertex;

        // One edge per UNDIRECTED pair, created on first use. A reversed traversal hands out the same
        // edge Reversed, which is what closes the shell without any spatial search.
        var edgeOf = new Dictionary<(int Lo, int Hi), TopoDS_Edge>(triangles.Length * 3 / 2);

        TopoDS_Edge EdgeOf(int from, int to)
        {
            var key = from < to ? (from, to) : (to, from);
            if (!edgeOf.TryGetValue(key, out var canonical))
            {
                canonical = new BRepBuilderAPI_MakeEdge(VertexOf(key.Item1), VertexOf(key.Item2)).Edge;
                edgeOf[key] = canonical;
            }
            return from < to ? canonical : (TopoDS_Edge)canonical.Reversed;
        }

        var builder = new BRep_Builder();          // MakeShell/Add are inherited from TopoDS_Builder
        builder.MakeShell(out var shell);

        var faces = new List<TopoDS_Face>(triangles.Length);
        foreach (int t in triangles)
        {
            int i0 = mesh.Indices[t * 3], i1 = mesh.Indices[t * 3 + 1], i2 = mesh.Indices[t * 3 + 2];
            if (i0 == i1 || i1 == i2 || i2 == i0) continue;      // degenerate: no wire can be made

            var wire = new BRepBuilderAPI_MakeWire();
            wire.Add(EdgeOf(i0, i1));
            wire.Add(EdgeOf(i1, i2));
            wire.Add(EdgeOf(i2, i0));
            if (!wire.IsDone) continue;

            var maker = new BRepBuilderAPI_MakeFace(wire.Wire);
            if (!maker.IsDone) continue;

            var face = maker.Face;
            faces.Add(face);
            builder.Add(shell, face);
        }

        if (faces.Count == 0) throw new InvalidOperationException("no face could be built from the triangles");

        var solidMaker = new BRepBuilderAPI_MakeSolid(shell);
        TopoDS_Shape shape = solidMaker.IsDone ? solidMaker.Solid : (TopoDS_Shape)shell;

        // Same discipline as SolidBuilder.Sew, in the same order: an invalid body is handed back
        // UNMERGED (a merge of a broken body buys nothing and has crashed OCCT), and `unify: false`
        // is a closed periodic body whose merged face does not transfer to STEP.
        if (!unify || !new BRepCheck_Analyzer(shape).IsValid())
            return Finish(shape, 1, CountFreeEdges(shape), CountFaces(shape));

        var unifier = new ShapeUpgrade_UnifySameDomain(shape, true, true, false);
        unifier.Build();
        shape = SolidBuilder.MergeOrKeepExact(unifier.Shape, shape);
        return Finish(shape, 1, CountFreeEdges(shape), CountFaces(shape));
    }

    /// <summary>
    /// True when every undirected edge of the triangle set is used exactly twice (and the set is
    /// non-empty). Pure mesh arithmetic - no OCCT - so the pipeline can decide before building.
    ///
    /// This is the load-bearing gate: an OPEN faceted set (the knob's 57,283 facets, 2,370 free
    /// edges) closes into a valid-but-open shell, not a solid, and its export carries a boundary
    /// loop. Those bodies keep the robust sewer.
    /// </summary>
    public static bool IsClosedSet(MeshData mesh, int[] triangles)
    {
        if (triangles.Length == 0) return false;

        var uses = new Dictionary<(int Lo, int Hi), int>(triangles.Length * 3 / 2);
        foreach (int t in triangles)
        {
            int i0 = mesh.Indices[t * 3], i1 = mesh.Indices[t * 3 + 1], i2 = mesh.Indices[t * 3 + 2];
            if (i0 == i1 || i1 == i2 || i2 == i0) return false;
            Use(uses, i0, i1);
            Use(uses, i1, i2);
            Use(uses, i2, i0);
        }

        foreach (int used in uses.Values)
            if (used != 2) return false;

        // ONE body, not several. Every edge can be used twice in a set that is four disjoint closed
        // bodies - clamp.stl is exactly that - and one shell around four bodies is one solid where
        // the sewer produced four: measured 5,784 faces / solids=1 where today is 4,444 / solids=4.
        // The shell path builds the WHOLE set into one shell, so it is only legal when the set is
        // one connected body.
        return IsOneBody(mesh, triangles);
    }

    /// <summary>
    /// True when the triangle set is a single component under EDGE adjacency: two triangles are
    /// connected when they use the same undirected mesh edge. Vertex contact is deliberately not a
    /// connection - clamp.stl's four bodies are welded at shared vertices (measured: bodies=4,
    /// RegionComponents.Group=4) and vertex union-find merges all four into one "body", which is the
    /// bug this rule exists to prevent. It is the same distinction RegionComponents.Group draws.
    /// </summary>
    private static bool IsOneBody(MeshData mesh, int[] triangles)
    {
        // Undirected edge -> the first triangle that used it. Union-find over triangles.
        var firstUse = new Dictionary<(int Lo, int Hi), int>(triangles.Length * 3 / 2);
        var parent = new int[triangles.Length];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;

        int Find(int i)
        {
            while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
            return i;
        }
        void Union(int a, int b)
        {
            int ra = Find(a), rb = Find(b);
            if (ra != rb) parent[rb] = ra;
        }

        for (int slot = 0; slot < triangles.Length; slot++)
        {
            int t = triangles[slot];
            int i0 = mesh.Indices[t * 3], i1 = mesh.Indices[t * 3 + 1], i2 = mesh.Indices[t * 3 + 2];
            Link(i0, i1, slot);
            Link(i1, i2, slot);
            Link(i2, i0, slot);
        }

        int root = Find(0);
        for (int i = 1; i < parent.Length; i++)
            if (Find(i) != root) return false;
        return true;

        // The first triangle to use an undirected edge owns it; the second joins it.
        void Link(int a, int b, int slot)
        {
            var key = a < b ? (a, b) : (b, a);
            if (firstUse.TryGetValue(key, out int other)) Union(other, slot);
            else firstUse[key] = slot;
        }
    }

    private static void Use(Dictionary<(int Lo, int Hi), int> uses, int a, int b)
    {
        var key = a < b ? (a, b) : (b, a);
        uses[key] = uses.TryGetValue(key, out int n) ? n + 1 : 1;
    }

    /// <summary>Volume, inversion guard and the verdict, for a finished shape.</summary>
    private static SolidResult Finish(TopoDS_Shape shape, int shells, int freeEdges, int faces)
    {
        BRepGProp.VolumeProperties(shape, out var props);
        if (props.Mass < 0)
        {
            // a shell built from mixed winding can come out inverted; fix by reversing, not by
            // trusting a flag - the same guard SolidBuilder.Sew applies after a sew
            shape.Reverse();
            BRepGProp.VolumeProperties(shape, out props);
        }
        bool valid = new BRepCheck_Analyzer(shape).IsValid();
        return new SolidResult(shape, shells, freeEdges, valid, props.Mass, faces);
    }

    /// <summary>
    /// Edges used by exactly one face - the count a sew would report as free. This must go through
    /// <c>TopExp.MapShapesAndUniqueAncestors</c>, which hashes by OCCT identity: <c>TopoDS_Edge</c>
    /// is not a valid <c>Dictionary</c> key (the managed wrapper compares by reference, and a census
    /// that does so reported 180,000 free edges on a 60,000-triangle shell where the truth is 0).
    /// </summary>
    private static int CountFreeEdges(TopoDS_Shape shape)
    {
        var map = new TopTools_IndexedDataMapOfShapeListOfShape();
        TopExp.MapShapesAndUniqueAncestors(shape, TopAbs_ShapeEnum.TopAbs_EDGE,
                                           TopAbs_ShapeEnum.TopAbs_FACE, out map);
        int single = 0;
        for (int i = 1; i <= map.Size; i++)
            if (map.FindFromIndex(i).Extent == 1) single++;
        return single;
    }

    private static int CountFaces(TopoDS_Shape shape)
    {
        int n = 0;
        var ex = new TopExp_Explorer(shape, TopAbs_ShapeEnum.TopAbs_FACE);
        while (ex.More) { n++; ex.Next(); }
        return n;
    }
}
