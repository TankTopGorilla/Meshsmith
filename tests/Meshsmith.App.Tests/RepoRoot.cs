using System.IO;
using System.Xml.Linq;

namespace Meshsmith.App.Tests;

internal static class RepoRoot
{
    public static string Path { get; } = Find();

    private static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, "Meshsmith.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Meshsmith.slnx not found above " + AppContext.BaseDirectory);
    }

    public static IEnumerable<string> XamlFiles() =>
        Directory.EnumerateFiles(System.IO.Path.Combine(Path, "src", "Meshsmith.App"), "*.xaml",
                                 SearchOption.AllDirectories);

    public static XDocument Load(string xamlPath) => XDocument.Load(xamlPath);
}
