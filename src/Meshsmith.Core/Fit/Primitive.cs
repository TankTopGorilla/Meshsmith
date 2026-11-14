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

public sealed record TorusPrimitive : Primitive
{
    public required Vec3 PointOnAxis { get; init; }
    public required Vec3 Axis { get; init; }
    public required double MajorRadius { get; init; }   // R: the tube-centre circle's radius
    public required double MinorRadius { get; init; }   // r: the tube's radius
    public override string Kind => "torus";

    /// <summary>
    /// Signed distance to the tube: the orthogonal projection onto the centre circle, then the
    /// distance to that circle's tube. Negative inside the tube, which is what the classifier's
    /// refit filter (|DistanceTo| &lt;= tol) expects - the same convention as CylinderPrimitive.
    /// The near-axis case is not special: rho = 0 projects to a point at distance R, so the
    /// result is sqrt(R^2 + z^2) - r and never divides by zero.
    /// </summary>
    public override double DistanceTo(Vec3 p)
    {
        var d = p - PointOnAxis;
        double z = Vec3.Dot(d, Axis);
        double rho = (d - Axis * z).Length;
        return Math.Sqrt((rho - MajorRadius) * (rho - MajorRadius) + z * z) - MinorRadius;
    }

    /// <summary>Outward normal: away from the nearest point of the centre circle.</summary>
    public override Vec3 NormalAt(Vec3 p)
    {
        var d = p - PointOnAxis;
        double z = Vec3.Dot(d, Axis);
        var radial = d - Axis * z;
        double rho = radial.Length;
        // On the axis the nearest tube point is the inner equator, so the normal is the in-plane
        // radial direction (any of them); pick a stable one via the basis rather than 0/0.
        var inPlane = rho < 1e-12 ? Fitters.Basis(Axis).E1 : radial / rho;
        // The nearest point of the CENTRE CIRCLE carries no axial component - it lies in the plane
        // through PointOnAxis. Adding Axis * z here would reconstruct the point directly inside p and
        // return a zero normal for every point on the tube.
        var centreCircle = PointOnAxis + inPlane * MajorRadius;
        return (p - centreCircle).Normalized();
    }
}

public sealed record SpherePrimitive : Primitive
{
    public required Vec3 Center { get; init; }
    public required double Radius { get; init; }
    public override string Kind => "sphere";
    public override double DistanceTo(Vec3 p) => (p - Center).Length - Radius;
    public override Vec3 NormalAt(Vec3 p) => (p - Center).Normalized();
}

public sealed record FacetedPrimitive : Primitive
{
    public required int Triangles { get; init; }
    public override string Kind => "faceted";
    public override double DistanceTo(Vec3 p) => double.NaN;
    public override Vec3 NormalAt(Vec3 p) => Vec3.Zero;
}
