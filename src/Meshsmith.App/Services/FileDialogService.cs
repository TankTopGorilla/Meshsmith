using System.IO;
using System.Windows;
using Meshsmith.Core.Import;
using Meshsmith.Core.Mesh;
using Microsoft.Win32;

namespace Meshsmith.App.Services;

public interface IFileDialogService
{
    string? PickStl();
    string? PickSave(string filter, string suggestedName, string? initialDirectory = null);
    string? PickFolder(string? initialDirectory);
}

public sealed class WpfFileDialogService : IFileDialogService
{
    /// <summary>Exactly what <see cref="MeshReaderRegistry"/> can open, in the order it tries the
    /// readers, so the dialog never offers a format the program would refuse. The first entry is the
    /// default: every existing project is an STL, and a grouped pattern keeps >1 format on one line.</summary>
    public const string MeshFilter =
        "Mesh files (*.stl;*.glb;*.gltf;*.fbx)|*.stl;*.glb;*.gltf;*.fbx|" +
        "STL meshes (*.stl)|*.stl|" +
        "glTF scenes (*.glb;*.gltf)|*.glb;*.gltf|" +
        "FBX meshes (*.fbx)|*.fbx|" +
        "All files (*.*)|*.*";

    private readonly Window _owner;

    public WpfFileDialogService(Window owner) => _owner = owner;

    public string? PickStl()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open mesh",
            Filter = MeshFilter,
            CheckFileExists = true
        };
        return dialog.ShowDialog(_owner) == true ? dialog.FileName : null;
    }

    public string? PickSave(string filter, string suggestedName, string? initialDirectory = null)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export",
            Filter = filter,
            FileName = suggestedName,
            OverwritePrompt = true
        };
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
            dialog.InitialDirectory = initialDirectory;

        return dialog.ShowDialog(_owner) == true ? dialog.FileName : null;
    }

    public string? PickFolder(string? initialDirectory)
    {
        var dialog = new OpenFolderDialog { Title = "Choose a default output folder" };
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
            dialog.InitialDirectory = initialDirectory;

        return dialog.ShowDialog(_owner) == true ? dialog.FolderName : null;
    }
}

/// <summary>Cheap facts about a mesh, read before any conversion starts. The file may be any format
/// the registry can open; the probe does not care which, it only wants triangles and bounds.</summary>
public readonly record struct MeshInfo(int TriangleCount, double SizeX, double SizeY, double SizeZ);

public interface IMeshProbe
{
    MeshInfo Describe(string path);
}

public sealed class MeshProbe : IMeshProbe
{
    public MeshInfo Describe(string path)
    {
        // One seam for every format: the registry decides what the file is (extension, then
        // signature) and returns a triangle soup already in millimetres. No conversion runs here -
        // this is the number the inspector shows before the user presses Convert.
        var read = MeshReaderRegistry.Read(path, new MeshReadOptions(),
                                           byteProgress: null, CancellationToken.None);
        var mesh = MeshWelder.Weld(read.Soup, 1e-5);
        var (min, max) = mesh.Bounds();
        return new MeshInfo(mesh.TriangleCount, max.X - min.X, max.Y - min.Y, max.Z - min.Z);
    }
}
