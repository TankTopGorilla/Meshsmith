using Meshsmith.Core.Brep;
using Meshsmith.Core.Fit;
using Meshsmith.Core.Geometry;
using Meshsmith.Core.Mesh;
using Occt;
using Xunit;

namespace Meshsmith.Core.Tests;

/// <summary>
/// A closed torus/sphere region has NO boundary loop, so FaceBuilder.Build throws "face has no
/// boundary" and the pipeline facets the whole body. The measured repair is a bounding uv rectangle
/// split once per periodic direction: 1x1 sews to 0 shells, while 1x2, 2x1 and 2x2 all sew to a VALID
/// solid with the exact volume (measured: torus 1776.528792, sphere 4188.790205 on R=10).
/// </summary>
public class TorusFaceTests
{
    [Fact]
    public void A_torus_surface_is_a_toroidal_surface_with_the_right_periods()
    {
        var surface = SurfaceFactory.Create(new TorusPrimitive
        {
            PointOnAxis = Vec3.Zero, Axis = new Vec3(0, 0, 1), MajorRadius = 10, MinorRadius = 3
        });
        var tor = Assert.IsType<Geom_ToroidalSurface>(surface);
        Assert.Equal(10, tor.MajorRadius, 6);
        Assert.Equal(3, tor.MinorRadius, 6);
        Assert.True(tor.IsUPeriodic && tor.IsVPeriodic);
        Assert.Equal(2 * Math.PI, tor.UPeriod, 6);
        Assert.Equal(2 * Math.PI, tor.VPeriod, 6);
    }

    [Fact]
    public void A_sphere_surface_is_a_spherical_surface_and_is_not_v_periodic()
    {
        var surface = SurfaceFactory.Create(new SpherePrimitive { Center = Vec3.Zero, Radius = 10 });
        var sph = Assert.IsType<Geom_SphericalSurface>(surface);
        Assert.Equal(10, sph.Radius, 6);
        Assert.True(sph.IsUPeriodic);
        Assert.False(sph.IsVPeriodic);            // V is latitude: poles are singular, not a seam
    }

    /// <summary>
    /// The closed-body ladder, end to end: build the faces the way the pipeline will, sew them, and
    /// demand the exact volume. This is the test that would have caught "one natural-bounds face".
    /// </summary>
    [Fact]
    public void A_closed_torus_builds_a_valid_solid_with_the_exact_volume()
    {
        var prim = new TorusPrimitive
        {
            PointOnAxis = Vec3.Zero, Axis = new Vec3(0, 0, 1), MajorRadius = 10, MinorRadius = 3
        };
        var faces = ClosedFaceBuilder.BuildClosed(prim, 0.02, Vec3.From(new gp_Pnt(13, 0, 0)), new Vec3(1, 0, 0));

        Assert.Equal(2, faces.Count);
        var sewed = SolidBuilder.SewRobust(faces, 0.02);
        Assert.True(sewed.IsValid);
        Assert.Equal(0, sewed.FreeEdges);
        Assert.Equal(2 * Math.PI * Math.PI * 10 * 3 * 3, sewed.Volume, 2);   // 1776.528792
    }

    [Fact]
    public void A_closed_sphere_builds_a_valid_solid_with_the_exact_volume()
    {
        var prim = new SpherePrimitive { Center = Vec3.Zero, Radius = 10 };
        var faces = ClosedFaceBuilder.BuildClosed(prim, 0.02, Vec3.From(new gp_Pnt(10, 0, 0)), new Vec3(1, 0, 0));

        Assert.Equal(2, faces.Count);
        var sewed = SolidBuilder.SewRobust(faces, 0.02);
        Assert.True(sewed.IsValid);
        Assert.Equal(4.0 / 3 * Math.PI * 1000, sewed.Volume, 2);             // 4188.790205
    }
}
