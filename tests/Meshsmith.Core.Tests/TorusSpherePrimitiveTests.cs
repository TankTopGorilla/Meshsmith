using Meshsmith.Core.Fit;
using Meshsmith.Core.Geometry;
using Xunit;

namespace Meshsmith.Core.Tests;

/// <summary>
/// The two new primitives are the contract the fits and the face builder read. A torus is the set of
/// points at distance r from a circle of radius R, so its DistanceTo is the signed distance to the TUBE
/// (orthogonal projection onto the centre circle), and its normal points away from that circle - the
/// axis-adjacent case is the one a naive implementation gets wrong.
/// </summary>
public class TorusSpherePrimitiveTests
{
    private static TorusPrimitive Torus(double R = 10, double r = 3) => new()
    {
        PointOnAxis = Vec3.Zero, Axis = new Vec3(0, 0, 1), MajorRadius = R, MinorRadius = r
    };

    [Fact]
    public void Torus_distance_is_zero_on_the_tube_and_signed_inside()
    {
        var t = Torus();

        // outer equator, inner equator, top of the tube, and a point on the tube at 45 degrees
        Assert.Equal(0.0, t.DistanceTo(new Vec3(13, 0, 0)), 9);
        Assert.Equal(0.0, t.DistanceTo(new Vec3(7, 0, 0)), 9);
        Assert.Equal(0.0, t.DistanceTo(new Vec3(10, 0, 3)), 9);
        double c = Math.Cos(Math.PI / 4), s = Math.Sin(Math.PI / 4);
        Assert.Equal(0.0, t.DistanceTo(new Vec3((10 + 3 * c) * c, (10 + 3 * c) * s, 3 * s)), 9);

        // the tube centre circle is at -r; the origin (on the axis) projects to rho = 0, so the
        // orthogonal distance to the centre circle is R and the tube distance is R - r
        Assert.Equal(-3.0, t.DistanceTo(new Vec3(10, 0, 0)), 6);      // tube centre circle
        Assert.Equal(7.0, t.DistanceTo(Vec3.Zero), 6);                // on the axis: R - r, signed
    }

    [Fact]
    public void Torus_normal_points_away_from_the_tube_centre_circle()
    {
        var t = Torus();
        Assert.Equal(1.0, Vec3.Dot(t.NormalAt(new Vec3(13, 0, 0)), new Vec3(1, 0, 0)), 6);
        Assert.Equal(-1.0, Vec3.Dot(t.NormalAt(new Vec3(7, 0, 0)), new Vec3(1, 0, 0)), 6);
        Assert.Equal(1.0, Vec3.Dot(t.NormalAt(new Vec3(10, 0, 3)), new Vec3(0, 0, 1)), 6);
        // on the axis the nearest tube point is the inner equator: the normal is radial again, i.e.
        // perpendicular to the axis and of unit length. Which in-plane direction it picks is arbitrary
        // (Fitters.Basis decides, measured (0,1,0) for axis +Z), so the axis dot is the stable claim.
        var onAxis = t.NormalAt(Vec3.Zero);
        Assert.Equal(0.0, Vec3.Dot(onAxis, new Vec3(0, 0, 1)), 6);
        Assert.Equal(1.0, onAxis.Length, 6);
    }

    [Fact]
    public void Sphere_distance_and_normal_are_trivial_and_exact()
    {
        var s = new SpherePrimitive { Center = new Vec3(1, 2, 3), Radius = 10 };
        Assert.Equal(0.0, s.DistanceTo(new Vec3(1, 2, 13)), 9);
        Assert.Equal(-5.0, s.DistanceTo(new Vec3(1, 2, 8)), 9);       // 5 INSIDE the sphere
        Assert.Equal(0.0, s.DistanceTo(new Vec3(1, 2, -7)), 9);       // 10 from the centre: on it
        Assert.Equal(1.0, Vec3.Dot(s.NormalAt(new Vec3(1, 2, 13)), new Vec3(0, 0, 1)), 6);
        Assert.Equal(-1.0, Vec3.Dot(s.NormalAt(new Vec3(1, 2, -7)), new Vec3(0, 0, 1)), 6);
    }

    [Fact]
    public void Both_declare_their_kind_and_survive_with_from_the_base()
    {
        var t = Torus();
        Assert.Equal("torus", t.Kind);
        Assert.Equal("sphere", new SpherePrimitive { Center = Vec3.Zero, Radius = 1 }.Kind);

        // `with` on the base must work: MergeSameSurface/Reprime rely on it.
        Primitive asBase = t;
        Assert.Equal(7, (asBase with { SourceTriangles = 7 }).SourceTriangles);
    }
}
