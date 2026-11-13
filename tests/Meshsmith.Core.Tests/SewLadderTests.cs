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
    public void Robust_sew_keeps_a_clean_box_valid()
    {
        var result = SolidBuilder.SewRobust(LiftedBox(0), 1e-6);

        Assert.Equal(0, result.FreeEdges);
        Assert.True(result.IsValid);
        Assert.Equal(6, result.Faces);
        Assert.Equal(1000.0, result.Volume, 3);
    }
}
