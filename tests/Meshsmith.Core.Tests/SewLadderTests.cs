using Meshsmith.Core.Brep;
using Occt;
using Xunit;

namespace Meshsmith.Core.Tests;

public class SewLadderTests
{
    /// <summary>A 10 mm cube whose top face is lifted by <paramref name="lift"/>: a closed shell with
    /// one crack exactly the width of a fit residual.</summary>
    private static List<TopoDS_Face> LiftedBox(double lift)
    {
        double s = 10, z = s + lift;
        return new List<TopoDS_Face>
        {
            Quad(new gp_Pnt(0, 0, 0), new gp_Pnt(0, s, 0), new gp_Pnt(s, s, 0), new gp_Pnt(s, 0, 0)),
            Quad(new gp_Pnt(0, 0, z), new gp_Pnt(s, 0, z), new gp_Pnt(s, s, z), new gp_Pnt(0, s, z)),
            Quad(new gp_Pnt(0, 0, 0), new gp_Pnt(s, 0, 0), new gp_Pnt(s, 0, s), new gp_Pnt(0, 0, s)),
            Quad(new gp_Pnt(s, 0, 0), new gp_Pnt(s, s, 0), new gp_Pnt(s, s, s), new gp_Pnt(s, 0, s)),
            Quad(new gp_Pnt(s, s, 0), new gp_Pnt(0, s, 0), new gp_Pnt(0, s, s), new gp_Pnt(s, s, s)),
            Quad(new gp_Pnt(0, s, 0), new gp_Pnt(0, 0, 0), new gp_Pnt(0, 0, s), new gp_Pnt(0, s, s))
        };
    }

    private static TopoDS_Face Quad(gp_Pnt a, gp_Pnt b, gp_Pnt c, gp_Pnt d)
    {
        var poly = new BRepBuilderAPI_MakePolygon(a, b, c, d, true);
        return new BRepBuilderAPI_MakeFace(poly.Wire).Face;
    }

    [Fact]
    public void Robust_sew_closes_a_crack_wider_than_the_first_tolerance()
    {
        var faces = LiftedBox(3e-6);

        var tight = SolidBuilder.Sew(faces, 1e-6);          // the control: the crack is real
        Assert.True(tight.FreeEdges > 0);
        Assert.False(tight.IsValid);

        var robust = SolidBuilder.SewRobust(faces, 1e-6);

        Assert.Equal(0, robust.FreeEdges);
        Assert.True(robust.IsValid);
        Assert.Equal(6, robust.Faces);
        Assert.Equal(1000.0, robust.Volume, 3);
    }

    [Fact]
    public void Robust_sew_does_not_escalate_a_closed_but_invalid_shell()
    {
        List<TopoDS_Face> Cube(double x0, double y0, double z0)
        {
            double x1 = x0 + 10, y1 = y0 + 10, z1 = z0 + 10;
            return new List<TopoDS_Face>
            {
                Quad(new gp_Pnt(x0, y0, z0), new gp_Pnt(x0, y1, z0), new gp_Pnt(x1, y1, z0), new gp_Pnt(x1, y0, z0)),
                Quad(new gp_Pnt(x0, y0, z1), new gp_Pnt(x1, y0, z1), new gp_Pnt(x1, y1, z1), new gp_Pnt(x0, y1, z1)),
                Quad(new gp_Pnt(x0, y0, z0), new gp_Pnt(x1, y0, z0), new gp_Pnt(x1, y0, z1), new gp_Pnt(x0, y0, z1)),
                Quad(new gp_Pnt(x1, y0, z0), new gp_Pnt(x1, y1, z0), new gp_Pnt(x1, y1, z1), new gp_Pnt(x1, y0, z1)),
                Quad(new gp_Pnt(x1, y1, z0), new gp_Pnt(x0, y1, z0), new gp_Pnt(x0, y1, z1), new gp_Pnt(x1, y1, z1)),
                Quad(new gp_Pnt(x0, y1, z0), new gp_Pnt(x0, y0, z0), new gp_Pnt(x0, y0, z1), new gp_Pnt(x0, y1, z1))
            };
        }

        // Three 10 mm cubes meeting on one shared vertical edge: the sew closes every edge, so
        // nothing is free, but three faces on that edge make the shell non-manifold and invalid.
        // Measured: free=0, valid=False, 18 faces, one shell, volume 1333.333 at 1x/2x/4x/8x alike.
        var faces = LiftedBox(0);
        faces.AddRange(Cube(-10, 0, 0));
        faces.AddRange(Cube(0, -10, 0));

        var tight = SolidBuilder.Sew(faces, 1e-6);
        Assert.Equal(0, tight.FreeEdges);
        Assert.False(tight.IsValid);

        var robust = SolidBuilder.SewRobust(faces, 1e-6);

        // the single sew's result, not a ladder rung: no tolerance can merge what is not free
        Assert.Equal(tight.FreeEdges, robust.FreeEdges);
        Assert.False(robust.IsValid);
        Assert.Equal(tight.Faces, robust.Faces);
        Assert.Equal(tight.Volume, robust.Volume, 3);
    }

    [Fact]
    public void Robust_sew_keeps_a_clean_box_valid()
    {
        var result = SolidBuilder.SewRobust(LiftedBox(0), 1e-6);

        Assert.Equal(0, result.FreeEdges);
        Assert.True(result.IsValid);
        Assert.Equal(6, result.Faces);
        Assert.Equal(1000.0, result.Volume, 3);
    }
}
