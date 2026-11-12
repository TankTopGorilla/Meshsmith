using System.IO;
using System.Windows;
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
    private readonly Window _owner;

    public WpfFileDialogService(Window owner) => _owner = owner;

    public string? PickStl()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open STL mesh",
            Filter = "STL meshes (*.stl)|*.stl|All files (*.*)|*.*",
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

/// <summary>Cheap facts about an STL, read before any conversion starts.</summary>
public readonly record struct MeshInfo(int TriangleCount, double SizeX, double SizeY, double SizeZ);

public interface IMeshProbe
{
    MeshInfo Describe(string stlPath);
}

public sealed class MeshProbe : IMeshProbe
{
    public MeshInfo Describe(string stlPath)
    {
        var mesh = MeshWelder.Weld(StlReader.Read(stlPath), 1e-5);
        var (min, max) = mesh.Bounds();
        return new MeshInfo(mesh.TriangleCount, max.X - min.X, max.Y - min.Y, max.Z - min.Z);
    }
}
