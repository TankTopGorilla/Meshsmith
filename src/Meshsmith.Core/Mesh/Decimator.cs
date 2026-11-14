using Meshsmith.Core.Geometry;

namespace Meshsmith.Core.Mesh;

/// <summary>
/// Bounded decimation for meshes too dense to classify. The classifer's fits are per-triangle and
/// its region growing is quadratic-ish in the triangle count, so a 386 k-triangle part never
/// finishes; a reduced mesh still carries the surfaces the fits have to see, which is why the
/// reduction happens BEFORE recognition and not after it.
///
/// geometry3Sharp's <see cref="g3.Reducer"/> is a quadric-error collapse: it minimizes the
/// deviation it introduces (that is the point of the quadric), preserves boundary shape, and keeps
/// the mesh closed on a closed input - measured on the 386,266-triangle knob: 60,000 triangles in
/// 1.08 s, still closed, one body.
/// </summary>
public static class Decimator
{
    /// <summary>Reduce to at most <paramref name="targetTriangles"/> triangles and re-establish the
    /// mesh invariants g3 can break at a collapse boundary (degenerate triangles, winding). The
    /// caller gets a mesh the rest of the pipeline can treat exactly like a read one.</summary>
    public static MeshData Decimate(MeshData mesh, int targetTriangles, RepairOptions? repair = null)
    {
        if (mesh.TriangleCount == 0) return mesh;

        // Nothing to do below ~8 triangles: a collapse that far has no surfaces left to fit, and g3
        // dislikes targets it cannot reach.
        int target = Math.Max(8, Math.Min(targetTriangles, mesh.TriangleCount));
        if (target >= mesh.TriangleCount) return mesh;

        var dm = new g3.DMesh3();
        foreach (var v in mesh.Vertices) dm.AppendVertex(new g3.Vector3d(v.X, v.Y, v.Z));
        for (int t = 0; t < mesh.TriangleCount; t++)
            dm.AppendTriangle(mesh.Indices[t * 3], mesh.Indices[t * 3 + 1], mesh.Indices[t * 3 + 2]);

        // One ReduceToTriangleCount call drains g3's collapse queue and can stop short of the target
        // (measured: a 16,384-triangle torus asked for 8,192 stopped at 8,502). A fresh reducer
        // rebuilds the queue on the collapsed topology and keeps it going; the loop stops the moment
        // a pass makes no progress, so a mesh that genuinely cannot collapse further is returned as
        // it is.
        for (int pass = 0; pass < 8 && dm.TriangleCount > target; pass++)
        {
            int before = dm.TriangleCount;
            var reducer = new g3.Reducer(dm)
            {
                PreserveBoundaryShape = true,
                MinimizeQuadricPositionError = true
            };
            reducer.ReduceToTriangleCount(target);
            if (dm.TriangleCount >= before) break;
        }

        // g3 keeps the vertices it collapsed away as dead slots; compact them so the index space
        // matches the vertex array the rest of the core expects.
        var vertices = new List<Vec3>(dm.VertexCount);
        var map = new int[dm.MaxVertexID];
        Array.Fill(map, -1);
        foreach (int vid in dm.VertexIndices())
        {
            var p = dm.GetVertex(vid);
            map[vid] = vertices.Count;
            vertices.Add(new Vec3(p.x, p.y, p.z));
        }

        var indices = new List<int>(dm.TriangleCount * 3);
        foreach (int ti in dm.TriangleIndices())
        {
            var tri = dm.GetTriangle(ti);
            indices.Add(map[tri.a]);
            indices.Add(map[tri.b]);
            indices.Add(map[tri.c]);
        }

        var reduced = new MeshData { Vertices = vertices.ToArray(), Indices = indices.ToArray() };

        // A quadric collapse can leave a sliver triangle where two collapses met. The existing repair
        // already drops degenerates, restores winding and closes what it can, so the decimated mesh
        // goes through the same door as a read one instead of a second, weaker path.
        var (repaired, _) = MeshRepair.Repair(reduced, repair ?? new RepairOptions());
        return repaired;
    }
}
