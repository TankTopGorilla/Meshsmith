using Occt;
using Xunit;

namespace Meshsmith.Core.Tests;

public class GoldenStepTests
{
    [Fact]
    [Trait("Category", "Golden")]
    public void Meshsmith_sample_has_the_published_census()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "reference_sample.step");
        if (!File.Exists(path)) return;   // optional fixture

        var reader = new STEPControl_Reader();
        Assert.Equal(IFSelect_ReturnStatus.IFSelect_RetDone, reader.ReadFile(path));
        Assert.True(reader.TransferRoots() > 0);
        var shape = reader.OneShape();

        int faces = 0, planes = 0, cylinders = 0, cones = 0;
        var ex = new TopExp_Explorer(shape, TopAbs_ShapeEnum.TopAbs_FACE);
        while (ex.More)
        {
            var a = new BRepAdaptor_Surface((TopoDS_Face)ex.Current);
            faces++;
            if (a.Type == GeomAbs_SurfaceType.GeomAbs_Plane) planes++;
            else if (a.Type == GeomAbs_SurfaceType.GeomAbs_Cylinder) cylinders++;
            else if (a.Type == GeomAbs_SurfaceType.GeomAbs_Cone) cones++;
            ex.Next();
        }

        Assert.Equal(301, faces);
        Assert.Equal(180, planes);
        Assert.Equal(118, cylinders);
        Assert.Equal(3, cones);
    }
}
