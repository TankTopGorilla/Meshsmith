using Occt;

namespace Meshsmith.Core.Export;

public static class IgesExporter
{
    // 1 = BRep mode (DE 186/514/510). 0 = face mode: trimmed surfaces only, and the file
    // reimports with ZERO solids - a silent failure. Never pass 0.
    private const int BrepMode = 1;
    private const string Unit = "MM";

    public static void Write(TopoDS_Shape shape, string path)
    {
        // The writer ctor initialises the IGES controller. If a first write ever fails with
        // an uninitialised-controller error, construct `new IGESControl_Controller()` and
        // call its instance method `.Init()` once before this writer.
        var writer = new IGESControl_Writer(Unit, BrepMode);

        if (!writer.AddShape(shape))
            throw new InvalidOperationException("IGES AddShape failed");
        if (!writer.Write(path))
            throw new InvalidOperationException("IGES write failed");
    }
}
