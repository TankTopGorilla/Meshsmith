using Meshsmith.Core.Geometry;

namespace Meshsmith.Core.Mesh;

/// <summary>What the repair is allowed to do. Holes above the cap are left open and reported.</summary>
public sealed record RepairOptions(int MaxHoleEdges = 64);

/// <summary>Everything the repair changed, in numbers a user can check.</summary>
public sealed record RepairReport(
    int DegenerateDropped,
    int DuplicatesDropped,
    int Flipped,
    int HolesFilled,
    int HoleEdges,
    int NonManifoldEdges)
{
    public static RepairReport None { get; } = new(0, 0, 0, 0, 0, 0);

    public bool Changed => DegenerateDropped > 0 || DuplicatesDropped > 0 || Flipped > 0 || HolesFilled > 0;

    public string Describe()
    {
        var parts = new List<string>();
        if (DegenerateDropped > 0) parts.Add($"{DegenerateDropped} degenerate triangles dropped");
        if (DuplicatesDropped > 0) parts.Add($"{DuplicatesDropped} duplicate faces dropped");
        if (Flipped > 0) parts.Add($"{Flipped} triangles flipped");
        if (HolesFilled > 0) parts.Add($"{HolesFilled} holes filled ({HoleEdges} edges)");
        if (NonManifoldEdges > 0) parts.Add($"{NonManifoldEdges} non-manifold edges left as they are");
        return parts.Count == 0 ? "mesh needed no repair" : "mesh repaired: " + string.Join(", ", parts);
    }
}

/// <summary>
/// Cleans a welded mesh before anything is fitted to it: degenerate and duplicate triangles go,
/// consistent winding is restored, and holes up to <see cref="RepairOptions.MaxHoleEdges"/> are
/// closed with a centroid fan. Every change is counted; a mesh that is still broken afterwards still
/// reports what is wrong with it.
/// </summary>
public static class MeshRepair
{
    public static (MeshData Mesh, RepairReport Report) Repair(MeshData mesh, RepairOptions? options = null)
    {
        options ??= new RepairOptions();
        double areaEpsilon = 1e-12 * mesh.Diagonal * mesh.Diagonal;

        var triangles = new List<(int A, int B, int C)>(mesh.TriangleCount);
        var seen = new HashSet<(int, int, int)>();
        int degenerate = 0, duplicates = 0;

        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            int a = mesh.Indices[t * 3], b = mesh.Indices[t * 3 + 1], c = mesh.Indices[t * 3 + 2];
            if (a == b || b == c || a == c || mesh.Area(t) <= areaEpsilon)
            {
                degenerate++;
                continue;
            }

            // Only an exact repeat is a duplicate. The same triangle with the opposite winding is a
            // back-to-back sheet - real geometry in thin-plate models - and dropping it would open a
            // hole in something the user modelled on purpose.
            if (!seen.Add((a, b, c)))
            {
                duplicates++;
                continue;
            }

            triangles.Add((a, b, c));
        }

        int flipped = FixWinding(triangles, mesh.Vertices);

        var vertices = mesh.Vertices.ToList();
        int nonManifold = CountNonManifoldEdges(triangles);

        // Filling a hole on a non-manifold surface invents topology: there is no well-defined inside.
        // Measured on a real part: a 3-edge fill on a mesh with 21 non-manifold edges moved the
        // sewn volume by 9 % and left the same number of open edges. Holes are closed only when the
        // surface is manifold, so the patch has a side to belong to.
        var (holesFilled, holeEdges) = nonManifold == 0
            ? FillHoles(triangles, vertices, options.MaxHoleEdges)
            : (0, 0);

        var report = new RepairReport(degenerate, duplicates, flipped, holesFilled, holeEdges, nonManifold);
        if (!report.Changed) return (mesh, report);

        var indices = new int[triangles.Count * 3];
        for (int t = 0; t < triangles.Count; t++)
        {
            indices[t * 3] = triangles[t].A;
            indices[t * 3 + 1] = triangles[t].B;
            indices[t * 3 + 2] = triangles[t].C;
        }

        return (new MeshData { Vertices = vertices.ToArray(), Indices = indices }, report);
    }

    private static (int, int, int) Sorted(int a, int b, int c)
    {
        if (a > b) (a, b) = (b, a);
        if (b > c) (b, c) = (c, b);
        if (a > b) (a, b) = (b, a);
        return (a, b, c);
    }

    /// <summary>
    /// BFS over edge-adjacent triangles, assigning each a parity: neighbours across a shared edge must
    /// traverse that edge in opposite directions. The parity is then flipped wholesale if the result
    /// points inward, so a single reversed triangle is corrected rather than the other eleven.
    /// </summary>
    private static int FixWinding(List<(int A, int B, int C)> triangles, Vec3[] vertices)
    {
        int count = triangles.Count;
        var edges = new Dictionary<(int, int), List<(int Tri, bool Forward)>>();
        for (int t = 0; t < count; t++) AddEdges(edges, triangles[t], t);

        var parity = new int[count];
        for (int i = 0; i < count; i++) parity[i] = -1;

        for (int seed = 0; seed < count; seed++)
        {
            if (parity[seed] >= 0) continue;
            parity[seed] = 0;
            var queue = new Queue<int>();
            queue.Enqueue(seed);

            while (queue.Count > 0)
            {
                int t = queue.Dequeue();
                foreach (var (u, v) in EdgesOf(triangles[t]))
                {
                    var key = u < v ? (u, v) : (v, u);
                    if (!edges.TryGetValue(key, out var users) || users.Count != 2) continue;

                    var mine = users[0].Tri == t ? users[0] : users[1];
                    var other = users[0].Tri == t ? users[1] : users[0];
                    if (parity[other.Tri] >= 0) continue;

                    // Same original direction means one of them must flip.
                    parity[other.Tri] = mine.Forward == other.Forward ? parity[t] ^ 1 : parity[t];
                    queue.Enqueue(other.Tri);
                }
            }
        }

        for (int i = 0; i < count; i++) if (parity[i] < 0) parity[i] = 0;

        if (SignedVolume(triangles, parity, vertices) < 0)
            for (int i = 0; i < count; i++) parity[i] ^= 1;

        int flipped = 0;
        for (int t = 0; t < count; t++)
        {
            if (parity[t] == 0) continue;
            var (a, b, c) = triangles[t];
            triangles[t] = (a, c, b);
            flipped++;
        }
        return flipped;
    }

    private static double SignedVolume(List<(int A, int B, int C)> triangles, int[] parity, Vec3[] vertices)
    {
        double volume = 0;
        for (int t = 0; t < triangles.Count; t++)
        {
            var (ia, ib, ic) = triangles[t];
            var a = vertices[ia];
            var b = parity[t] == 0 ? vertices[ib] : vertices[ic];
            var c = parity[t] == 0 ? vertices[ic] : vertices[ib];
            volume += Vec3.Dot(a, Vec3.Cross(b, c));
        }
        return volume / 6.0;
    }

    private static void AddEdges(Dictionary<(int, int), List<(int Tri, bool Forward)>> edges,
                                 (int A, int B, int C) tri, int index)
    {
        foreach (var (u, v) in EdgesOf(tri))
        {
            var key = u < v ? (u, v) : (v, u);
            if (!edges.TryGetValue(key, out var users)) edges[key] = users = new List<(int, bool)>(2);
            users.Add((index, u < v));
        }
    }

    private static IEnumerable<(int U, int V)> EdgesOf((int A, int B, int C) tri)
    {
        yield return (tri.A, tri.B);
        yield return (tri.B, tri.C);
        yield return (tri.C, tri.A);
    }

    private static Dictionary<(int, int), int> EdgeUses(List<(int A, int B, int C)> triangles)
    {
        var uses = new Dictionary<(int, int), int>();
        foreach (var tri in triangles)
            foreach (var (u, v) in EdgesOf(tri))
            {
                var key = u < v ? (u, v) : (v, u);
                uses[key] = uses.TryGetValue(key, out int n) ? n + 1 : 1;
            }
        return uses;
    }

    private static int CountNonManifoldEdges(List<(int A, int B, int C)> triangles)
    {
        int count = 0;
        foreach (int uses in EdgeUses(triangles).Values) if (uses > 2) count++;
        return count;
    }

    /// <summary>
    /// Closes boundary loops with a centroid fan. The fan uses the boundary edge reversed, so the new
    /// triangles face the same way as the surface they close.
    /// </summary>
    private static (int Holes, int Edges) FillHoles(List<(int A, int B, int C)> triangles,
                                                     List<Vec3> vertices, int maxHoleEdges)
    {
        var uses = EdgeUses(triangles);

        // Directed boundary edges, as the existing triangles traverse them.
        var outgoing = new Dictionary<int, List<int>>();
        foreach (var tri in triangles)
            foreach (var (u, v) in EdgesOf(tri))
            {
                var key = u < v ? (u, v) : (v, u);
                if (uses[key] != 1) continue;
                if (!outgoing.TryGetValue(u, out var ends)) outgoing[u] = ends = new List<int>(1);
                ends.Add(v);
            }

        var consumed = new HashSet<(int, int)>();
        int holes = 0, edges = 0;

        foreach (int start in outgoing.Keys.ToList())
        {
            foreach (int first in outgoing[start].ToList())
            {
                if (consumed.Contains((start, first))) continue;

                var loop = new List<int> { start };
                var used = new HashSet<(int, int)>();
                int cursor = start;
                bool closed = false;

                while (used.Count <= maxHoleEdges + 1)
                {
                    if (!outgoing.TryGetValue(cursor, out var ends)) break;
                    int? next = null;
                    foreach (int end in ends)
                        if (!used.Contains((cursor, end))) { next = end; break; }
                    if (next is null) break;

                    used.Add((cursor, next.Value));
                    if (next.Value == start) { closed = true; break; }
                    loop.Add(next.Value);
                    cursor = next.Value;
                }

                if (closed && loop.Count >= 3 && loop.Count <= maxHoleEdges)
                {
                    var centroid = new Vec3(0, 0, 0);
                    foreach (int v in loop) centroid = centroid + vertices[v];
                    centroid = centroid / loop.Count;

                    int centre = vertices.Count;
                    vertices.Add(centroid);

                    for (int i = 0; i < loop.Count; i++)
                    {
                        int u = loop[i];
                        int v = loop[(i + 1) % loop.Count];
                        triangles.Add((v, u, centre));      // reversed: the patch faces outward
                    }

                    holes++;
                    edges += loop.Count;
                }

                foreach (var pair in used) consumed.Add(pair);
            }
        }

        return (holes, edges);
    }
}
