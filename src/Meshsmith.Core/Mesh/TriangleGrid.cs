using Meshsmith.Core.Geometry;

namespace Meshsmith.Core.Mesh;

public sealed class TriangleGrid
{
    private readonly MeshData _mesh;
    private readonly (Vec3 Min, Vec3 Max) _bounds;
    private readonly double _cell;
    private readonly int _nx, _ny, _nz;
    private readonly List<int>?[] _cells;

    public TriangleGrid(MeshData mesh)
    {
        _mesh = mesh;
        _bounds = mesh.Bounds();
        var size = _bounds.Max - _bounds.Min;
        double maxDim = Math.Max(size.X, Math.Max(size.Y, size.Z));
        _cell = Math.Max(maxDim / 32.0, 1e-9);
        _nx = Math.Max(1, (int)Math.Ceiling(size.X / _cell));
        _ny = Math.Max(1, (int)Math.Ceiling(size.Y / _cell));
        _nz = Math.Max(1, (int)Math.Ceiling(size.Z / _cell));
        _cells = new List<int>?[_nx * _ny * _nz];

        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            var (a, b, c) = mesh.Tri(t);
            var lo = Min(Min(a, b), c);
            var hi = Max(Max(a, b), c);
            var i0 = Index(lo); var i1 = Index(hi);
            for (int z = i0.z; z <= i1.z; z++)
            for (int y = i0.y; y <= i1.y; y++)
            for (int x = i0.x; x <= i1.x; x++)
            {
                int k = (z * _ny + y) * _nx + x;
                (_cells[k] ??= new List<int>()).Add(t);
            }
        }
    }

    public double DistanceToSurface(Vec3 p)
    {
        double best = double.MaxValue;
        var start = Index(p);
        int maxRing = Math.Max(_nx, Math.Max(_ny, _nz));

        for (int ring = 0; ring <= maxRing; ring++)
        {
            for (int z = start.z - ring; z <= start.z + ring; z++)
            for (int y = start.y - ring; y <= start.y + ring; y++)
            for (int x = start.x - ring; x <= start.x + ring; x++)
            {
                if (x < 0 || y < 0 || z < 0 || x >= _nx || y >= _ny || z >= _nz) continue;
                var cell = _cells[(z * _ny + y) * _nx + x];
                if (cell == null) continue;
                foreach (var t in cell)
                {
                    var (a, b, c) = _mesh.Tri(t);
                    best = Math.Min(best, PointTriangleDistance(p, a, b, c));
                }
            }
            // a found distance is only valid if no unvisited ring could be closer
            if (best < ring * _cell) return best;
        }
        return best;
    }

    private (int x, int y, int z) Index(Vec3 p)
    {
        var d = p - _bounds.Min;
        return (Clamp((int)(d.X / _cell), _nx), Clamp((int)(d.Y / _cell), _ny), Clamp((int)(d.Z / _cell), _nz));
    }

    private static int Clamp(int v, int n) => Math.Clamp(v, 0, n - 1);
    private static Vec3 Min(Vec3 a, Vec3 b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
    private static Vec3 Max(Vec3 a, Vec3 b) => new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));

    public static double PointTriangleDistance(Vec3 p, Vec3 a, Vec3 b, Vec3 c)
    {
        // Christer Ericson, Real-Time Collision Detection, sect. 5.1.5
        var ab = b - a; var ac = c - a; var ap = p - a;
        double d1 = Vec3.Dot(ab, ap), d2 = Vec3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return (p - a).Length;

        var bp = p - b;
        double d3 = Vec3.Dot(ab, bp), d4 = Vec3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return (p - b).Length;

        double vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0)
        {
            double v = d1 / (d1 - d3);
            return (p - (a + ab * v)).Length;
        }

        var cp = p - c;
        double d5 = Vec3.Dot(ab, cp), d6 = Vec3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return (p - c).Length;

        double vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0)
        {
            double w = d2 / (d2 - d6);
            return (p - (a + ac * w)).Length;
        }

        double va = d3 * d6 - d5 * d4;
        if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0)
        {
            double w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
            return (p - (b + (c - b) * w)).Length;
        }

        double denom = 1.0 / (va + vb + vc);
        double vv = vb * denom, ww = vc * denom;
        return (p - (a + ab * vv + ac * ww)).Length;
    }
}
