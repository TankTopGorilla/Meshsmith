using Meshsmith.Core.Brep;
using Meshsmith.Core.Fit;
using Meshsmith.Core.Geometry;
using Meshsmith.Core.Mesh;
using Meshsmith.Core.Pipeline;
using Meshsmith.Core.Seg;
using Occt;
using Xunit;

namespace Meshsmith.Core.Tests;

public class BRepTests
{
    [Fact]
    public void Cube_face_has_one_loop_of_four_vertices()
    {
        var mesh = MeshWelder.Weld(StlReader.Read(StlFixtures.WriteCube(10)), 1e-6);
        var topo = MeshTopology.Build(mesh);
        var faces = PrimitiveClassifier.Classify(mesh, PrimitiveClassifier.Options.Default(0.01));
        var plane = faces.First(f => f.Primitive is PlanePrimitive p && Math.Abs(p.Normal.Z) > 0.9 && p.Point.Z > 5);
        var loops = BoundaryLoops.Extract(mesh, topo, plane.Triangles);
        Assert.Single(loops);
        Assert.Equal(4, loops[0].Length);
    }

    [Fact]
    public void Washer_top_face_has_two_loops()
    {
        var mesh = MeshWelder.Weld(StlReader.Read(StlFixtures.WriteWasher(30, 10, 10, 64)), 1e-6);
        var topo = MeshTopology.Build(mesh);
        var faces = PrimitiveClassifier.Classify(mesh, PrimitiveClassifier.Options.Default(0.02));
        var top = faces.First(f => f.Primitive is PlanePrimitive p && Math.Abs(p.Normal.Z) > 0.9 && p.Point.Z > 5);
        Assert.Equal(2, BoundaryLoops.Extract(mesh, topo, top.Triangles).Count);
    }

    [Fact]
    public void Builds_square_face_with_expected_area()
    {
        var surface = new Geom_Plane(new gp_Pln(new gp_Pnt(0, 0, 0), new gp_Dir(0, 0, 1)));
        var plane = new PlanePrimitive { Point = new Vec3(0, 0, 0), Normal = new Vec3(0, 0, 1) };
        // the loop must already be closed (UvProjector appends the closing point)
        var loop = new (double, double)[] { (0, 0), (10, 0), (10, 10), (0, 10), (0, 0) };
        var face = Assert.Single(FaceBuilder.Build(surface, plane, new[] { loop }, uvTol: 1e-6,
                                     samplePoint: new Vec3(5, 5, 0), sampleNormal: new Vec3(0, 0, 1)));

        // NOTE: the Occt.NET wrapper's BRepGProp.SurfaceProperties returns the PERIMETER
        // (it binds to LinearProperties), so area must be read from TopoDS_Shape.Area.
        Assert.Equal(100.0, face.Area, 6);
    }

    [Fact]
    public void Sews_box_faces_into_one_valid_solid()
    {
        var box = new BRepPrimAPI_MakeBox(10, 10, 10).Solid;
        var faces = new List<TopoDS_Face>();
        var ex = new TopExp_Explorer(box, TopAbs_ShapeEnum.TopAbs_FACE);
        while (ex.More) { faces.Add((TopoDS_Face)ex.Current); ex.Next(); }

        var result = SolidBuilder.Sew(faces, 1e-6);
        Assert.Equal(1, result.Shells);
        Assert.Equal(0, result.FreeEdges);
        Assert.True(result.IsValid);
        Assert.Equal(1000.0, result.Volume, 3);
    }
}

public class PipelineTests
{
    [Fact]
    public void Cube_converts_to_a_valid_solid()
    {
        var result = ConversionPipeline.Run(StlFixtures.WriteCube(10),
            new ConversionOptions { KeepTemporaryFiles = true });
        Assert.True(result.IsValidSolid, string.Join("; ", result.Warnings));
        Assert.Equal(1, result.SolidCount);
        Assert.Equal(6, result.FaceCount);
        Assert.Equal(1000.0, result.Volume, 2);
        Assert.True(result.SurfaceDeviation <= 1e-6);
    }
}
