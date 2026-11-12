using Meshsmith.Core.Fit;
using Meshsmith.Core.Geometry;
using Meshsmith.Core.Mesh;
using Meshsmith.Core.Seg;
using Xunit;

namespace Meshsmith.Core.Tests;

public class FitTests
{
    [Fact]
    public void Fits_plane_through_known_points()
    {
        var pts = new[] { new Vec3(0,0,5), new Vec3(10,0,5), new Vec3(0,10,5), new Vec3(10,10,5) };
        var plane = Fitters.FitPlane(pts, 1e-6);
        Assert.NotNull(plane);
        Assert.Equal(5.0, plane!.Point.Z, 6);
        Assert.Equal(1.0, Math.Abs(plane.Normal.Z), 9);
    }

    [Fact]
    public void Fits_cylinder_axis_and_radius()
    {
        var mesh = MeshWelder.Weld(StlReader.Read(StlFixtures.WriteCylinder(20, 30, 256)), 1e-6);
        var topo = MeshTopology.Build(mesh);
        var side = RegionGrower.Grow(mesh, topo, 12).OrderByDescending(r => r.Length).First();

        var fit = PrimitiveClassifier.FitRegion(mesh, side, tol: 0.05, diag: mesh.Diagonal);
        var cyl = Assert.IsType<CylinderPrimitive>(fit);
        Assert.Equal(20.0, cyl.Radius, 2);
        Assert.Equal(1.0, Math.Abs(Vec3.Dot(cyl.Axis, new Vec3(0, 0, 1))), 6);
    }

    [Fact]
    public void Fits_a_region_that_has_more_vertices_than_triangles()
    {
        // Two facets of a cylinder's side: six vertices, four triangles. The cylinder and cone fits
        // walk a point list and a normal list together, so those lists must be built as a pair.
        // Passing one normal per triangle made FitCylinder index past its end on real text meshes.
        const double radius = 10, height = 10;
        const int facets = 12;
        double span = Math.PI / 3;                      // 60 degrees of arc

        var vertices = new List<Vec3>();
        for (int i = 0; i <= facets; i++)
        {
            double a = span * i / facets;
            vertices.Add(new Vec3(radius * Math.Cos(a), radius * Math.Sin(a), 0));
            vertices.Add(new Vec3(radius * Math.Cos(a), radius * Math.Sin(a), height));
        }

        var indices = new List<int>();
        for (int i = 0; i < facets; i++)
        {
            int b0 = i * 2, b1 = (i + 1) * 2;
            indices.AddRange(new[] { b0, b1, b1 + 1 });
            indices.AddRange(new[] { b0, b1 + 1, b0 + 1 });
        }

        // 26 vertices, 24 triangles: the point list is longer than the normal list used to be.
        var mesh = new MeshData { Vertices = vertices.ToArray(), Indices = indices.ToArray() };

        var fit = PrimitiveClassifier.FitRegion(mesh, Enumerable.Range(0, facets * 2).ToArray(),
                                                tol: 0.05, diag: mesh.Diagonal);

        var cylinder = Assert.IsType<CylinderPrimitive>(fit);
        Assert.InRange(cylinder.Radius, radius * 0.95, radius * 1.05);   // 12 facets bias it slightly
    }

    [Fact]
    public void Fits_cone_half_angle()
    {
        var mesh = MeshWelder.Weld(StlReader.Read(StlFixtures.WriteCone(30, 10, 20, 256)), 1e-6);
        var topo = MeshTopology.Build(mesh);
        var side = RegionGrower.Grow(mesh, topo, 12).OrderByDescending(r => r.Length).First();

        var fit = PrimitiveClassifier.FitRegion(mesh, side, tol: 0.05, diag: mesh.Diagonal);
        var cone = Assert.IsType<ConePrimitive>(fit);
        double expected = Math.Atan((30 - 10) / 20.0);   // 45 degrees
        Assert.Equal(expected, cone.HalfAngle, 2);
    }

    [Fact]
    public void Cube_gives_six_planes_with_zero_residual()
    {
        var mesh = MeshWelder.Weld(StlReader.Read(StlFixtures.WriteCube(10)), 1e-6);
        var faces = PrimitiveClassifier.Classify(mesh, PrimitiveClassifier.Options.Default(0.01));
        Assert.Equal(6, faces.Count);
        Assert.All(faces, f => Assert.Equal("plane", f.Primitive.Kind));
        Assert.All(faces, f => Assert.True(f.Primitive.FitMax <= 1e-6));
    }

    [Fact]
    public void Cylinder_gives_three_regions()
    {
        var mesh = MeshWelder.Weld(StlReader.Read(StlFixtures.WriteCylinder(20, 30, 256)), 1e-6);
        var faces = PrimitiveClassifier.Classify(mesh, PrimitiveClassifier.Options.Default(0.02));
        Assert.Equal(3, faces.Count);
        Assert.Single(faces, f => f.Primitive.Kind == "cylinder");
        Assert.Equal(2, faces.Count(f => f.Primitive.Kind == "plane"));
    }

    [Fact]
    public void Washer_gives_two_cylinders_and_two_planes()
    {
        var mesh = MeshWelder.Weld(StlReader.Read(StlFixtures.WriteWasher(30, 10, 10, 256)), 1e-6);
        var faces = PrimitiveClassifier.Classify(mesh, PrimitiveClassifier.Options.Default(0.02));
        Assert.Equal(4, faces.Count);
        Assert.Equal(2, faces.Count(f => f.Primitive.Kind == "cylinder"));
        Assert.Equal(2, faces.Count(f => f.Primitive.Kind == "plane"));
    }
}
