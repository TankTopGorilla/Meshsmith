using Meshsmith.Core.Geometry;
using Meshsmith.Core.Mesh;
using Occt;

namespace Meshsmith.Core.Brep;

public static class FacetedBuilder
{
    public static List<TopoDS_Face> Build(MeshData mesh, int[] triangles)
    {
        var faces = new List<TopoDS_Face>(triangles.Length);
        foreach (var t in triangles)
        {
            var (a, b, c) = mesh.Tri(t);
            var poly = new BRepBuilderAPI_MakePolygon(a.ToPnt(), b.ToPnt(), c.ToPnt(), true);
            var face = new BRepBuilderAPI_MakeFace(poly.Wire).Face;
            faces.Add(face);
        }
        return faces;
    }
}
