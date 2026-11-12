using Meshsmith.Core.Geometry;

namespace Meshsmith.Core.Mesh;

/// <summary>Raw STL output: 3 unindexed vertices per triangle, in file order.</summary>
public sealed class TriangleSoup
{
    public required Vec3[] Points { get; init; }     // length == 3 * TriangleCount
    public int TriangleCount => Points.Length / 3;
}

public sealed class MeshData
{
    public required Vec3[] Vertices { get; init; }
    public required int[] Indices { get; init; }      // 3 per triangle
    public int TriangleCount => Indices.Length / 3;

    public Vec3 V(int tri, int corner) => Vertices[Indices[tri * 3 + corner]];
    public Vec3 V0(int t) => Vertices[Indices[t * 3]];
    public Vec3 V1(int t) => Vertices[Indices[t * 3 + 1]];
    public Vec3 V2(int t) => Vertices[Indices[t * 3 + 2]];
    public (Vec3 A, Vec3 B, Vec3 C) Tri(int t) => (V0(t), V1(t), V2(t));

    public Vec3 Normal(int t)
    {
        var (a, b, c) = Tri(t);
        return Vec3.Cross(b - a, c - a).Normalized();
    }

    public Vec3 Centroid(int t) { var (a, b, c) = Tri(t); return (a + b + c) / 3.0; }

    public double Area(int t) { var (a, b, c) = Tri(t); return Vec3.Cross(b - a, c - a).Length * 0.5; }

    public (Vec3 Min, Vec3 Max) Bounds()
    {
        var min = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue);
        var max = new Vec3(double.MinValue, double.MinValue, double.MinValue);
        foreach (var v in Vertices)
        {
            min = new Vec3(Math.Min(min.X, v.X), Math.Min(min.Y, v.Y), Math.Min(min.Z, v.Z));
            max = new Vec3(Math.Max(max.X, v.X), Math.Max(max.Y, v.Y), Math.Max(max.Z, v.Z));
        }
        return (min, max);
    }

    public double Diagonal { get { var (mn, mx) = Bounds(); return (mx - mn).Length; } }

    public double SignedVolume()
    {
        double v = 0;
        for (int t = 0; t < TriangleCount; t++)
        {
            var (a, b, c) = Tri(t);
            v += Vec3.Dot(a, Vec3.Cross(b, c));
        }
        return v / 6.0;
    }
}
