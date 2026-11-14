using Meshsmith.Core;
using Meshsmith.Core.Import;

namespace Meshsmith.Core.Tests;

/// <summary>FBX through AssimpNet. The one thing assimp does not give us is the unit: Blender writes
/// UnitScaleFactor = 1.0 no matter what the scene says (five fixtures, §2 p4), so the reader warns
/// every single time and the default is millimetres - the only guess the user can undo with a toggle.</summary>
public class FbxReaderTests
{
    [Fact]
    public void Blender_fbx_cube_is_12_triangles_and_applies_the_node_transform()
    {
        var read = Read("cube_10.fbx");

        Assert.Equal("FBX", read.Format);
        Assert.Equal(12, read.Soup.TriangleCount);       // 6 quads, triangulated by the post-process step

        // Measured, not assumed: the mesh vertices are ±5 (the probe's v0), but assimp's scene carries
        // the exporter's 100x cm scaling in the node transform, so the traversed part is 1000 units
        // across. Applying that transform is the plan's §3.4 algorithm; the plan's "10 mm" expectation
        // came from the untransformed vertex and is 100x short. With the default (numbers are mm) the
        // part is 1000 mm - hence the warning on every FBX read.
        var (min, max) = GltfReaderTests.Bounds(read.Soup);
        Assert.Equal(1000.0, max.X - min.X, 3);
        Assert.Equal(1000.0, max.Y - min.Y, 3);
        Assert.Equal(1000.0, max.Z - min.Z, 3);
        Assert.Equal(1.0, read.AppliedScale);
    }

    [Fact]
    public void Fbx_units_are_reported_as_unknown_when_the_file_says_one()
    {
        var read = Read("cube_10.fbx");

        Assert.Contains(read.Warnings, w => w.Contains("assuming millimetres", StringComparison.Ordinal));
        Assert.Equal("FBX · unit not declared · treated as millimetres", read.UnitNote);
    }

    [Fact]
    public void Fbx_honours_the_treat_as_millimetres_setting()
    {
        var asMm = Read("cube_10.fbx");                                            // null = format default
        var asMetres = Read("cube_10.fbx", new MeshReadOptions { TreatNumbersAsMillimetres = false });

        Assert.Equal(1000.0, asMetres.AppliedScale);
        Assert.Contains("×1000", asMetres.UnitNote);

        var (mmMin, mmMax) = GltfReaderTests.Bounds(asMm.Soup);
        var (mMin, mMax) = GltfReaderTests.Bounds(asMetres.Soup);
        Assert.Equal(1000.0 * (mmMax.X - mmMin.X), mMax.X - mMin.X, 3);
    }

    [Fact]
    public void A_missing_assimp_dll_is_a_named_failure()
    {
        string bogus = Path.Combine(Path.GetTempPath(), "no-assimp-" + Guid.NewGuid().ToString("N"), "assimp.dll");
        NativeLoader.AssimpPath = bogus;
        try
        {
            var ex = Assert.Throws<MeshFormatException>(() => Read("cube_10.fbx"));
            Assert.Contains("FBX support is not installed", ex.Message);
        }
        finally
        {
            NativeLoader.AssimpPath = null;
        }
    }

    [Fact]
    public void An_fbx_with_no_meshes_is_a_named_failure()
    {
        var ex = Assert.Throws<MeshFormatException>(() => Read("empty.fbx"));

        Assert.Equal("no triangle geometry", ex.Feature);
        Assert.Contains("node", ex.Message);
    }

    // ---- helpers ----------------------------------------------------------------

    private static MeshReadResult Read(string name, MeshReadOptions? options = null) =>
        MeshReaderRegistry.Read(Fixture(name), options ?? new MeshReadOptions());

    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "import", name);
}
