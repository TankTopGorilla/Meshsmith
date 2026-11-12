using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Meshsmith.Core.Mesh;
using HelixToolkit.Wpf;
using Occt;

namespace Meshsmith.App;

/// <summary>Builds the WPF 3D scene. Every group is frozen so it can cross threads.</summary>
public static class MeshPreview
{
    // These colours are mirrored by Tokens.xaml Kind* brushes — keep them in sync.
    private static readonly Color Steel = Color.FromRgb(0x7E, 0x8A, 0x99);
    private static readonly Color Plane = Color.FromRgb(0x93, 0xA3, 0xB5);
    private static readonly Color Cylinder = Color.FromRgb(0xE3, 0xA4, 0x55);
    private static readonly Color Cone = Color.FromRgb(0x7F, 0xC0, 0x8D);
    private static readonly Color Faceted = Color.FromRgb(0xE2, 0x70, 0x5E);

    public static Model3DGroup FromStl(string path)
    {
        var mesh = MeshWelder.Weld(StlReader.Read(path), 1e-5);
        var geometry = new MeshGeometry3D();
        foreach (var vertex in mesh.Vertices) geometry.Positions.Add(new Point3D(vertex.X, vertex.Y, vertex.Z));
        for (int i = 0; i < mesh.Indices.Length; i++) geometry.TriangleIndices.Add(mesh.Indices[i]);
        geometry.Freeze();

        var group = new Model3DGroup();
        group.Children.Add(new GeometryModel3D(geometry, MaterialFor(Steel, 18)));
        group.Freeze();
        return group;
    }

    /// <summary>Tessellates the exported STEP and colours each face by its surface kind.</summary>
    public static Model3DGroup FromStep(string path)
    {
        var reader = new STEPControl_Reader();
        reader.ReadFile(path);
        reader.TransferRoots();
        var shape = reader.OneShape();

        _ = new BRepMesh_IncrementalMesh(shape, 0.2, false, 0.35);
        var group = new Model3DGroup();

        var explorer = new TopExp_Explorer(shape, TopAbs_ShapeEnum.TopAbs_FACE);
        while (explorer.More)
        {
            var face = (TopoDS_Face)explorer.Current;
            var triangulation = BRep_Tool.Triangulation(face, out _);
            if (triangulation != null && triangulation.NbNodes > 0)
            {
                var geometry = new MeshGeometry3D();
                for (int i = 1; i <= triangulation.NbNodes; i++)
                {
                    var point = triangulation.Node(i);
                    geometry.Positions.Add(new Point3D(point.X, point.Y, point.Z));
                }
                for (int i = 1; i <= triangulation.NbTriangles; i++)
                {
                    var triangle = triangulation.Triangle(i);
                    geometry.TriangleIndices.Add(triangle.Value(1) - 1);
                    geometry.TriangleIndices.Add(triangle.Value(2) - 1);
                    geometry.TriangleIndices.Add(triangle.Value(3) - 1);
                }
                geometry.Freeze();

                var kind = new BRepAdaptor_Surface(face).Type;
                var colour = kind switch
                {
                    GeomAbs_SurfaceType.GeomAbs_Plane => Plane,
                    GeomAbs_SurfaceType.GeomAbs_Cylinder => Cylinder,
                    GeomAbs_SurfaceType.GeomAbs_Cone => Cone,
                    _ => Faceted
                };
                group.Children.Add(new GeometryModel3D(geometry, MaterialFor(colour, 26)));
            }
            explorer.Next();
        }

        group.Freeze();
        return group;
    }

    private static Material MaterialFor(Color colour, double specularPower)
    {
        var diffuseBrush = new SolidColorBrush(colour);
        diffuseBrush.Freeze();
        var specularBrush = new SolidColorBrush(Color.FromRgb(0xB8, 0xC8, 0xDA));
        specularBrush.Freeze();

        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(diffuseBrush));
        material.Children.Add(new SpecularMaterial(specularBrush, specularPower));
        material.Freeze();
        return material;
    }
}
