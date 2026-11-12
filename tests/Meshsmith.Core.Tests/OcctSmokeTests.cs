using Occt;
using Xunit;

namespace Meshsmith.Core.Tests;

public class OcctSmokeTests
{
    [Fact]
    public void Box_roundtrips_through_step()
    {
        var box = new BRepPrimAPI_MakeBox(10, 20, 30).Solid;
        var path = Path.Combine(Path.GetTempPath(), $"box-{Guid.NewGuid():N}.step");

        var writer = new STEPControl_Writer();
        Assert.Equal(IFSelect_ReturnStatus.IFSelect_RetDone,
            writer.Transfer(box, STEPControl_StepModelType.STEPControl_AsIs));
        Assert.Equal(IFSelect_ReturnStatus.IFSelect_RetDone, writer.Write(path));

        var reader = new STEPControl_Reader();
        Assert.Equal(IFSelect_ReturnStatus.IFSelect_RetDone, reader.ReadFile(path));
        Assert.True(reader.TransferRoots() > 0);
        var shape = reader.OneShape();

        BRepGProp.VolumeProperties(shape, out var props);
        Assert.Equal(6000.0, props.Mass, 6);

        File.Delete(path);
    }
}
