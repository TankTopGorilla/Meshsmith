using Meshsmith.Core.Geometry;

namespace Meshsmith.Core.Fit;

public abstract record Primitive
{
    public abstract string Kind { get; }                 // "plane" | "cylinder" | "cone" | "faceted"
    public abstract double DistanceTo(Vec3 p);           // signed distance to the infinite surface
    public abstract Vec3 NormalAt(Vec3 p);               // surface normal at the projected point

    // Stats are ordinary init properties on the base: derived records set them in object
    // initialisers and `with { ... }` works from the base type.
    public double FitMax { get; init; }
    public double FitMean { get; init; }
    public int SourceTriangles { get; init; }
}

public sealed record PlanePrimitive : Primitive
{
    public required Vec3 Point { get; init; }
    public required Vec3 Normal { get; init; }
    public override string Kind => "plane";
    public override double DistanceTo(Vec3 p) => Vec3.Dot(p - Point, Normal);
    public override Vec3 NormalAt(Vec3 p) => Normal;
}

public sealed record CylinderPrimitive : Primitive
{
    public required Vec3 PointOnAxis { get; init; }
    public required Vec3 Axis { get; init; }
    public required double Radius { get; init; }
    public override string Kind => "cylinder";

    public override double DistanceTo(Vec3 p)
    {
        var d = p - PointOnAxis;
        var axial = Vec3.Dot(d, Axis);
        return (d - Axis * axial).Length - Radius;
    }

    public override Vec3 NormalAt(Vec3 p)
    {
        var d = p - PointOnAxis;
        return (d - Axis * Vec3.Dot(d, Axis)).Normalized();
    }
}

public sealed record ConePrimitive : Primitive
{
    public required Vec3 Apex { get; init; }
    public required Vec3 Axis { get; init; }          // points away from the apex into the cone
    public required double HalfAngle { get; init; }   // radians
    public override string Kind => "cone";

    public override double DistanceTo(Vec3 p)
    {
        var d = p - Apex;
        double z = Vec3.Dot(d, Axis);
        double rho = (d - Axis * z).Length;
        return Math.Abs(rho - Math.Abs(z) * Math.Tan(HalfAngle));
    }

    public override Vec3 NormalAt(Vec3 p)
    {
        var d = p - Apex;
        double z = Vec3.Dot(d, Axis);
        var radial = (d - Axis * z).Normalized();
        return (radial * Math.Cos(HalfAngle) - Axis * Math.Sign(z) * Math.Sin(HalfAngle)).Normalized();
    }
}

public sealed record FacetedPrimitive : Primitive
{
    public required int Triangles { get; init; }
    public override string Kind => "faceted";
    public override double DistanceTo(Vec3 p) => double.NaN;
    public override Vec3 NormalAt(Vec3 p) => Vec3.Zero;
}
