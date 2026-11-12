namespace Meshsmith.Core.Mesh;

/// <summary>
/// What a mesh is really like before anything is fitted to it: holes, edges shared by more than
/// two triangles, and how many separate bodies it holds. Fitting cannot repair a broken mesh, so
/// the report has to say so up front instead of letting the sewing stage take the blame later.
/// </summary>
public sealed record MeshHealth(int OpenEdges, int NonManifoldEdges, int Bodies)
{
    public bool IsWatertight => OpenEdges == 0 && NonManifoldEdges == 0;

    public static MeshHealth Measure(MeshData mesh, MeshTopology topology)
    {
        // Count distinct edges, not triangle corners: a watched edge shared by three triangles
        // would otherwise be reported twice as a hole and once as non-manifold, and the number
        // would disagree with every mesh checker the user has.
        var uses = new Dictionary<(int A, int B), int>();
        for (int t = 0; t < mesh.TriangleCount; t++)
            for (int k = 0; k < 3; k++)
            {
                int a = mesh.Indices[t * 3 + k];
                int b = mesh.Indices[t * 3 + (k + 1) % 3];
                if (a == b) continue;
                var key = a < b ? (a, b) : (b, a);
                uses[key] = uses.TryGetValue(key, out int seen) ? seen + 1 : 1;
            }

        int open = 0, nonManifold = 0;
        foreach (int count in uses.Values)
        {
            if (count == 1) open++;
            else if (count > 2) nonManifold++;
        }

        var parent = new int[mesh.TriangleCount];
        for (int t = 0; t < parent.Length; t++) parent[t] = t;

        int Find(int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }
            return x;
        }

        for (int t = 0; t < mesh.TriangleCount; t++)
            for (int e = 0; e < 3; e++)
            {
                int neighbour = topology.Neighbor[t * 3 + e];
                if (neighbour < 0) continue;
                int a = Find(t), b = Find(neighbour);
                if (a != b) parent[a] = b;
            }

        var bodies = new HashSet<int>();
        for (int t = 0; t < mesh.TriangleCount; t++) bodies.Add(Find(t));

        return new MeshHealth(open, nonManifold, bodies.Count);
    }
}
