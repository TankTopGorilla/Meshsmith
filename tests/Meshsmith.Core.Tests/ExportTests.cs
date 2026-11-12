using Meshsmith.Core.Report;
using System.Text.Json;
using Meshsmith.Core.Export;
using Occt;
using Xunit;

namespace Meshsmith.Core.Tests;

public class ExportTests
{
    [Fact]
    public void Iges_roundtrips_a_solid_in_brep_mode()
    {
        var box = new BRepPrimAPI_MakeBox(10, 20, 30).Solid;
        var path = Path.Combine(Path.GetTempPath(), $"box-{Guid.NewGuid():N}.igs");
        IgesExporter.Write(box, path);

        var reader = new IGESControl_Reader();
        Assert.Equal(IFSelect_ReturnStatus.IFSelect_RetDone, reader.ReadFile(path));
        Assert.True(reader.TransferRoots() > 0);
        Assert.True(reader.NbShapes > 0);          // the only trustworthy success signal

        var back = reader.OneShape();
        BRepGProp.VolumeProperties(back, out var props);
        Assert.Equal(6000.0, props.Mass, 3);
        Assert.True(new BRepCheck_Analyzer(back).IsValid());

        File.Delete(path);
    }

    [Fact]
    public void Iges_reader_can_say_done_for_garbage_so_shapes_are_the_real_gate()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "this is not an iges file");

        var reader = new IGESControl_Reader();
        reader.ReadFile(path);                      // may return RetDone - do not assert on it
        var roots = reader.TransferRoots();

        Assert.True(roots == 0 || reader.NbShapes == 0);
        File.Delete(path);
    }

    [Fact]
    public void Report_uses_the_documented_field_names()
    {
        var json = JsonSerializer.Serialize(new ConversionReport { ResultKind = "prismatic" });
        foreach (var key in new[] { "result_kind", "output_faces", "surface_types", "faceted_triangles",
                                    "fit_tolerance", "surface_deviation", "elapsed_seconds" })
            Assert.Contains($"\"{key}\"", json);
    }
}
