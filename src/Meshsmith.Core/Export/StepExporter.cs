using Occt;

namespace Meshsmith.Core.Export;

public static class StepExporter
{
    public static void Write(TopoDS_Shape shape, string path)
    {
        var writer = new STEPControl_Writer();
        var transfer = writer.Transfer(shape, STEPControl_StepModelType.STEPControl_AsIs);
        if (transfer != IFSelect_ReturnStatus.IFSelect_RetDone)
            throw new InvalidOperationException($"STEP transfer failed: {transfer}");
        if (writer.Write(path) != IFSelect_ReturnStatus.IFSelect_RetDone)
            throw new InvalidOperationException("STEP write failed");
    }
}
