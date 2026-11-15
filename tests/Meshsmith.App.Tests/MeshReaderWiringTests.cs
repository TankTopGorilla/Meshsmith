using System.IO;
using System.Threading;
using System.Windows.Media.Media3D;
using Meshsmith.App.Services;
using Meshsmith.Core.Import;

namespace Meshsmith.App.Tests;

/// <summary>P10a put glTF/GLB/FBX readers in Core behind <see cref="MeshReaderRegistry"/>; this is the
/// proof the shell actually calls them. Before this wiring the dialog offered STL only and the probe
/// fed an FBX to the STL parser, so a release claiming FBX support could not open one.</summary>
public class MeshReaderWiringTests
{
    /// <summary>The App tests source fixtures from the repository, not from the build output.</summary>
    private static string Fixture(string name) =>
        Path.Combine(RepoRoot.Path, "fixtures", "import", name);

    [Fact]
    public void The_dialog_filter_offers_all_four_mesh_formats()
    {
        string filter = WpfFileDialogService.MeshFilter;

        Assert.Contains("*.stl", filter);
        Assert.Contains("*.glb", filter);
        Assert.Contains("*.gltf", filter);
        Assert.Contains("*.fbx", filter);
    }

    [Fact]
    public void The_probe_reads_a_gltf_file()
    {
        // moved.gltf is a 12-triangle cube authored at 0.01 units, i.e. metres: the probe must show
        // 12 triangles and a 10 mm box. A 10 mm box is only possible if the registry's reader applied
        // the ×1000 the format declares; the node's 100 m translation moves the box without resizing it.
        var info = new MeshProbe().Describe(Fixture("moved.gltf"));

        Assert.Equal(12, info.TriangleCount);
        Assert.Equal(10.0, info.SizeX, 3);
        Assert.Equal(10.0, info.SizeY, 3);
        Assert.Equal(10.0, info.SizeZ, 3);
    }

    [Fact]
    public void The_preview_reads_a_gltf_file()
    {
        // WPF's Freezables are DispatcherObjects; the viewport builds them on the UI thread, so the
        // preview is exercised on an STA thread rather than xunit's MTA worker.
        Model3DGroup group = RunSta(() => MeshPreview.FromStl(Fixture("moved.gltf")));

        Assert.NotEmpty(group.Children);

        var model = Assert.IsType<GeometryModel3D>(group.Children[0]);
        var geometry = Assert.IsType<MeshGeometry3D>(model.Geometry);
        Assert.Equal(36, geometry.TriangleIndices.Count);   // 12 triangles survived the weld
        Assert.NotEmpty(geometry.Positions);                // and the preview is not an empty shell
    }

    [Fact]
    public void An_unsupported_format_is_a_named_error()
    {
        var path = Path.Combine(Path.GetTempPath(), $"meshsmith-ui-test-{Guid.NewGuid():N}.xyz");
        File.WriteAllText(path, "not a mesh at all\n");
        try
        {
            // The registry names the formats it tried so the error card says something actionable
            // instead of leaking a parser exception from whichever reader happened to be first.
            var ex = Assert.Throws<MeshFormatException>(() => new MeshProbe().Describe(path));
            Assert.Equal("unrecognised format", ex.Feature);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Runs a WPF-object factory on a single-threaded apartment and rethrows what it captured.</summary>
    private static T RunSta<T>(Func<T> factory)
    {
        T result = default!;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try { result = factory(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null) throw new InvalidOperationException("STA worker failed", failure);
        return result;
    }
}
