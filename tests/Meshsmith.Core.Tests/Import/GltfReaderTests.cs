using Meshsmith.Core.Geometry;
using Meshsmith.Core.Import;
using Meshsmith.Core.Mesh;

namespace Meshsmith.Core.Tests;

/// <summary>glTF/GLB through SharpGLTF: the topologies a real Blender export can contain, the
/// transforms that must be applied exactly once, and the metres-to-mm conversion that must be loud.</summary>
public class GltfReaderTests
{
    [Fact]
    public void Glb_cube_is_12_triangles_and_the_unit_is_converted()
    {
        var read = Read("cube_10.glb");

        Assert.Equal("GLB", read.Format);
        Assert.Equal(12, read.Soup.TriangleCount);

        // The plan's §5 line expected a 10 mm bbox here; measured, the Blender cube is 10 UNITS in the
        // file (±5 accessor, §2 p1) and glTF units are metres, so x1000 puts it at 10,000 mm. The 10 mm
        // proof is cube_metres.glb (authored at 0.01), asserted in the test below.
        var (min, max) = Bounds(read.Soup);
        Assert.Equal(10_000.0, max.X - min.X, 3);
        Assert.Equal(10_000.0, max.Y - min.Y, 3);
        Assert.Equal(10_000.0, max.Z - min.Z, 3);
        Assert.Contains("×1000", read.UnitNote);
        Assert.Equal(1000.0, read.AppliedScale);
    }

    [Fact]
    public void Gltf_json_cube_matches_the_glb()
    {
        var glb = Read("cube_10.glb");
        var json = Read("cube_10.gltf");

        Assert.Equal("glTF", json.Format);
        Assert.Equal(glb.Soup.TriangleCount, json.Soup.TriangleCount);

        var (glbMin, glbMax) = Bounds(glb.Soup);
        var (jsonMin, jsonMax) = Bounds(json.Soup);
        Assert.Equal(glbMin.X, jsonMin.X, 6);
        Assert.Equal(glbMin.Y, jsonMin.Y, 6);
        Assert.Equal(glbMin.Z, jsonMin.Z, 6);
        Assert.Equal(glbMax.X, jsonMax.X, 6);
        Assert.Equal(glbMax.Y, jsonMax.Y, 6);
        Assert.Equal(glbMax.Z, jsonMax.Z, 6);
    }

    [Fact]
    public void A_metres_file_is_scaled_to_mm_and_the_note_says_so()
    {
        // The loud one: the file is a 0.01-unit cube, so a 1000x silent shrink would show up here as
        // 0.01 mm - the assertion is the bbox in millimetres, not "it did not throw".
        var read = Read("cube_metres.glb");

        Assert.Equal(12, read.Soup.TriangleCount);
        var (min, max) = Bounds(read.Soup);
        Assert.Equal(10.0, max.X - min.X, 2);
        Assert.Equal(10.0, max.Y - min.Y, 2);
        Assert.Equal(10.0, max.Z - min.Z, 2);
        Assert.Equal(1000.0, read.AppliedScale);
        Assert.Contains("×1000", read.UnitNote);
    }

    [Fact]
    public void A_triangle_strip_is_expanded_to_triangles()
    {
        var read = Read("strip.gltf");

        Assert.Equal(2, read.Soup.TriangleCount);
        Assert.Empty(read.Warnings);
    }

    [Fact]
    public void A_triangle_fan_is_expanded()
    {
        var read = Read("fan.gltf");

        Assert.Equal(3, read.Soup.TriangleCount);
        Assert.Empty(read.Warnings);
    }

    [Fact]
    public void A_node_transform_is_applied_once()
    {
        // Translation 100 on the node. Applying the world matrix twice puts the cube at 200.
        var read = Read("moved.gltf");
        var (min, max) = Bounds(read.Soup);

        Assert.Equal(95.0, min.X, 4);
        Assert.Equal(105.0, max.X, 4);
    }

    [Fact]
    public void A_two_instance_scene_expands_to_24_triangles_and_warns()
    {
        var read = Read("inst.gltf");

        Assert.Equal(24, read.Soup.TriangleCount);   // one 12-triangle cube, used by two nodes
        Assert.Equal(1, read.InstancesExpanded);     // 2 nodes - 1 mesh
        Assert.NotEmpty(read.Warnings);
        Assert.Contains(read.Warnings, w => w.Contains("instance", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Draco_is_a_named_failure()
    {
        var ex = Assert.Throws<MeshFormatException>(() => Read("draco.glb"));

        Assert.Contains("KHR_draco_mesh_compression", ex.Message);
        Assert.Equal("KHR_draco_mesh_compression", ex.Feature);
    }

    [Fact]
    public void Points_and_lines_are_skipped_with_a_warning()
    {
        var read = Read("points_lines.gltf");

        Assert.Equal(2, read.Soup.TriangleCount);    // only the TRIANGLES primitive
        Assert.Contains(read.Warnings, w => w.Contains("POINTS", StringComparison.Ordinal));
        Assert.Contains(read.Warnings, w => w.Contains("LINES", StringComparison.Ordinal));
    }

    // ---- helpers ----------------------------------------------------------------

    private static MeshReadResult Read(string name) =>
        MeshReaderRegistry.Read(Fixture(name), new MeshReadOptions());

    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "import", name);

    internal static (Vec3 Min, Vec3 Max) Bounds(TriangleSoup soup)
    {
        var min = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue);
        var max = new Vec3(double.MinValue, double.MinValue, double.MinValue);
        foreach (var p in soup.Points)
        {
            min = new Vec3(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
            max = new Vec3(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
        }
        return (min, max);
    }
}
