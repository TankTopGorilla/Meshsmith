using Meshsmith.Core.Geometry;

namespace Meshsmith.Core.Mesh;

public static class MeshWelder
{
    public static MeshData Weld(TriangleSoup soup, double tolerance)
    {
        if (tolerance <= 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
        var inv = 1.0 / tolerance;
        var map = new Dictionary<(long, long, long), int>(soup.Points.Length);
        var verts = new List<Vec3>(soup.Points.Length);
        var idx = new int[soup.Points.Length];

        for (int i = 0; i < soup.Points.Length; i++)
        {
            var p = soup.Points[i];
            var key = ((long)Math.Round(p.X * inv),
                       (long)Math.Round(p.Y * inv),
                       (long)Math.Round(p.Z * inv));
            if (!map.TryGetValue(key, out int vi))
            {
                vi = verts.Count;
                verts.Add(p);
                map[key] = vi;
            }
            idx[i] = vi;
        }

        // Drop triangles that collapsed to a line/point during welding.
        var keep = new List<int>(idx.Length);
        for (int t = 0; t < idx.Length; t += 3)
        {
            int a = idx[t], b = idx[t + 1], c = idx[t + 2];
            if (a == b || b == c || a == c) continue;
            keep.Add(a); keep.Add(b); keep.Add(c);
        }
        return new MeshData { Vertices = verts.ToArray(), Indices = keep.ToArray() };
    }
}
