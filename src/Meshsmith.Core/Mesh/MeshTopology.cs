namespace Meshsmith.Core.Mesh;

public sealed class MeshTopology
{
    /// <summary>[tri*3 + edge] -> neighbour triangle index, or -1.</summary>
    public required int[] Neighbor { get; init; }
    public required bool[] NonManifold { get; init; }
    public int EdgeCount { get; init; }

    public static MeshTopology Build(MeshData m)
    {
        var map = new Dictionary<(int, int), (int Tri, int Edge)>();
        var neighbor = Enumerable.Repeat(-1, m.TriangleCount * 3).ToArray();
        var nonManifold = new bool[m.TriangleCount];
        int edges = 0;

        for (int t = 0; t < m.TriangleCount; t++)
        {
            for (int e = 0; e < 3; e++)
            {
                int a = m.Indices[t * 3 + e];
                int b = m.Indices[t * 3 + (e + 1) % 3];
                var key = a < b ? (a, b) : (b, a);

                if (!map.TryGetValue(key, out var first))
                {
                    map[key] = (t, e);
                    edges++;
                }
                else if (neighbor[first.Tri * 3 + first.Edge] == -1)
                {
                    neighbor[first.Tri * 3 + first.Edge] = t;
                    neighbor[t * 3 + e] = first.Tri;
                }
                else
                {
                    nonManifold[t] = true;
                    nonManifold[first.Tri] = true;
                }
            }
        }
        return new MeshTopology { Neighbor = neighbor, NonManifold = nonManifold, EdgeCount = edges };
    }
}
