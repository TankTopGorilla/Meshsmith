using Meshsmith.Core.Mesh;

namespace Meshsmith.Core.Brep;

public static class BoundaryLoops
{
    /// <summary>Ordered vertex-index loops around a set of triangles, in mesh winding order.</summary>
    public static List<int[]> Extract(MeshData mesh, MeshTopology topo, int[] triangles)
    {
        var inside = new HashSet<int>(triangles);
        var next = new Dictionary<int, List<int>>();

        foreach (var t in triangles)
            for (int e = 0; e < 3; e++)
            {
                int a = mesh.Indices[t * 3 + e];
                int b = mesh.Indices[t * 3 + (e + 1) % 3];
                if (a == b) continue;
                int nb = topo.Neighbor[t * 3 + e];
                if (nb >= 0 && inside.Contains(nb)) continue;
                if (!next.TryGetValue(a, out var list)) next[a] = list = new List<int>(1);
                list.Add(b);
            }

        var loops = new List<int[]>();
        while (next.Count > 0)
        {
            int start = next.Keys.First();
            int cur = start;
            var loop = new List<int> { start };

            while (true)
            {
                if (!next.TryGetValue(cur, out var outs) || outs.Count == 0)
                    throw new InvalidOperationException($"open boundary at vertex {cur}");

                int nxt = outs[^1];
                outs.RemoveAt(outs.Count - 1);
                if (outs.Count == 0) next.Remove(cur);

                if (nxt == start) break;
                loop.Add(nxt);
                cur = nxt;
                if (loop.Count > 5_000_000) throw new InvalidOperationException("runaway boundary walk");
            }
            loops.Add(loop.ToArray());
        }
        return loops;
    }
}
