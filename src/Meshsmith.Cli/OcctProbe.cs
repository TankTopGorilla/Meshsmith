using Occt;

namespace Meshsmith.Core;

public static class OcctProbe
{
    public static int PrintCensus(string path)
    {
        bool isIges = path.EndsWith(".igs", StringComparison.OrdinalIgnoreCase)
                   || path.EndsWith(".iges", StringComparison.OrdinalIgnoreCase);

        TopoDS_Shape shape;
        if (isIges)
        {
            var iges = new IGESControl_Reader();
            iges.ReadFile(path);                 // may report done on garbage
            var roots = iges.TransferRoots();
            if (roots == 0 || iges.NbShapes == 0)
            {
                Console.Error.WriteLine("no IGES shapes");
                return 4;
            }
            shape = iges.OneShape();
        }
        else
        {
            var reader = new STEPControl_Reader();
            if (reader.ReadFile(path) != IFSelect_ReturnStatus.IFSelect_RetDone)
            {
                Console.Error.WriteLine("read failed");
                return 3;
            }
            if (reader.TransferRoots() == 0)
            {
                Console.Error.WriteLine("no transferable roots");
                return 4;
            }
            shape = reader.OneShape();
        }

        int faces = 0, planes = 0, cylinders = 0, cones = 0, torus = 0, spheres = 0, other = 0;

        var ex = new TopExp_Explorer(shape, TopAbs_ShapeEnum.TopAbs_FACE);
        while (ex.More)
        {
            var face = (TopoDS_Face)ex.Current;
            var adaptor = new BRepAdaptor_Surface(face);
            faces++;
            switch (adaptor.Type)
            {
                case GeomAbs_SurfaceType.GeomAbs_Plane: planes++; break;
                case GeomAbs_SurfaceType.GeomAbs_Cylinder: cylinders++; break;
                case GeomAbs_SurfaceType.GeomAbs_Cone: cones++; break;
                case GeomAbs_SurfaceType.GeomAbs_Torus: torus++; break;
                case GeomAbs_SurfaceType.GeomAbs_Sphere: spheres++; break;
                default: other++; break;
            }
            ex.Next();   // MUST be unconditional
        }

        BRepGProp.VolumeProperties(shape, out var props);

        Console.WriteLine($"faces={faces} plane={planes} cylinder={cylinders} cone={cones} torus={torus} sphere={spheres} other={other}");
        Console.WriteLine($"volume={props.Mass:F3} valid={new BRepCheck_Analyzer(shape).IsValid()}");
        return 0;
    }
}
