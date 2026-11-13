using Meshsmith.Core.Fit;
using Meshsmith.Core.Mesh;

namespace Meshsmith.Core.Pipeline;

public static class RegionComponents
{
    /// <summary>
    /// Groups regions into the bodies they belong to: two regions land in one component when they share
    /// a mesh edge that is a boundary edge of both. Vertex contact does not count - a shared vertex is
    /// how two bodies touch in a mesh that was welded at a corner, not how a shell is sewn.
    /// </summary>
    public static List<int[]> Group(MeshData mesh, MeshTopology topo, IReadOnlyList<ClassifiedRegion> regions)
    {
        int n = regions.Count;
        var parent = Enumerable.Range(0, n).ToArray();

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

        var owner = Enumerable.Repeat(-1, mesh.TriangleCount).ToArray();
        for (int r = 0; r < n; r++)
            foreach (var t in regions[r].Triangles) owner[t] = r;

        // A mesh edge that is a boundary half-edge of two different regions is the seam they share.
        var firstOwner = new Dictionary<(int, int), int>();
        for (int r = 0; r < n; r++)
        {
            foreach (var t in regions[r].Triangles)
            {
                for (int e = 0; e < 3; e++)
                {
                    int a = mesh.Indices[t * 3 + e];
                    int b = mesh.Indices[t * 3 + (e + 1) % 3];
                    int nb = topo.Neighbor[t * 3 + e];
                    if (nb >= 0 && owner[nb] == r) continue;          // interior to this region

                    var key = a < b ? (a, b) : (b, a);
                    if (firstOwner.TryGetValue(key, out int other)) Union(other, r);
                    else firstOwner[key] = r;
                }
            }
        }

        var buckets = new SortedDictionary<int, List<int>>();
        for (int r = 0; r < n; r++)
        {
            int root = Find(r);
            if (!buckets.TryGetValue(root, out var list)) buckets[root] = list = new List<int>();
            list.Add(r);
        }
        return buckets.Values.Select(l => l.ToArray()).ToList();
    }
}
