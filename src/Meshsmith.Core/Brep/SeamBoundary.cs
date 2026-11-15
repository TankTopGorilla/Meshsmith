using Meshsmith.Core.Mesh;

namespace Meshsmith.Core.Brep;

/// <summary>
/// Which boundary edges of a region sit on a seam with a body that is NOT analytic. Those edges must be
/// built as the mesh polyline: the faceted side is one straight edge per mesh edge between the mesh's own
/// vertices, so a fitted curve on this side has no partner to sew to (E1 probe, ATLAS component 4:
/// 8 of 13 free edges on the analytic-faceted seam, all within 1 µm of the mesh edge).
/// </summary>
public static class SeamBoundary
{
    /// <summary>True for the mesh edge (a,b) when the triangle across it belongs to a region whose
    /// faces are faceted (null entry) - i.e. the seam cannot be closed by fitted curves. An edge with
    /// no triangle across it is a seam too: an open mesh boundary has nothing that could ever pair
    /// with it, so pretending it is a fitted curve would only hide the hole.</summary>
    public static bool[] Mark(MeshData mesh, MeshTopology topo,
                              IReadOnlyList<int> loop, Func<int, bool> regionIsFaceted,
                              Func<int, int> triangleRegion)
    {
        if (loop.Count == 0) return Array.Empty<bool>();

        // Directed half-edge -> owner triangle, built once per loop: marking is then O(triangles +
        // loop) instead of O(triangles * loop). ATLAS marks 105 analytic regions over 3,940 triangles
        // and the T1196 control 1,828 regions over 32,385, so the difference is real.
        var owner = new Dictionary<long, int>(mesh.TriangleCount * 3);
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            for (int e = 0; e < 3; e++)
            {
                int a = mesh.Indices[t * 3 + e];
                int b = mesh.Indices[t * 3 + (e + 1) % 3];
                if (a == b) continue;
                owner.TryAdd(Key(a, b), t * 3 + e);
            }
        }

        var seam = new bool[loop.Count];
        for (int i = 0; i < loop.Count; i++)
        {
            int a = loop[i];
            int b = loop[(i + 1) % loop.Count];
            if (a == b) continue;

            // The loop is a walk of this region's own boundary half-edges, so (a,b) must be one. If it
            // is not, the caller handed us a loop that does not belong to this mesh: stay conservative
            // and keep today's fitted curve rather than making a claim we cannot check.
            if (!owner.TryGetValue(Key(a, b), out int halfEdge)) continue;

            int across = topo.Neighbor[halfEdge];
            if (across < 0) { seam[i] = true; continue; }

            int region = triangleRegion(across);
            seam[i] = region < 0 || regionIsFaceted(region);
        }
        return seam;
    }

    private static long Key(int a, int b) => ((long)a << 32) | (uint)b;
}

/// <summary>
/// One boundary loop's seam mask (index-aligned with the loop's mesh edges: entry i describes the edge
/// loop[i] -> loop[i+1]) together with the mesh the loop was walked on, so the face builder can put the
/// straight seam edge on the mesh's own vertices instead of on the fitted surface.
/// </summary>
public sealed record SeamLoop(MeshData Mesh, int[] Vertices, bool[] Seam);
