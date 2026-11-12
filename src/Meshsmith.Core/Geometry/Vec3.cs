using Occt;

namespace Meshsmith.Core.Geometry;

public readonly record struct Vec3(double X, double Y, double Z)
{
    public static readonly Vec3 Zero = new(0, 0, 0);

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);
    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator /(Vec3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);

    public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    public static Vec3 Cross(Vec3 a, Vec3 b) =>
        new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    public double Length => Math.Sqrt(Dot(this, this));
    public Vec3 Normalized() { var l = Length; return l < 1e-15 ? Zero : this / l; }
    public double this[int i] => i switch { 0 => X, 1 => Y, _ => Z };
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);

    public gp_Pnt ToPnt() => new(X, Y, Z);
    public static Vec3 From(gp_Pnt p) => new(p.X, p.Y, p.Z);

    public static Vec3 Mean(IEnumerable<Vec3> pts)
    {
        double x = 0, y = 0, z = 0; int n = 0;
        foreach (var p in pts) { x += p.X; y += p.Y; z += p.Z; n++; }
        return n == 0 ? Zero : new Vec3(x / n, y / n, z / n);
    }
}
