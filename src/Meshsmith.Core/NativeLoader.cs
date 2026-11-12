using System.Runtime.InteropServices;

namespace Meshsmith.Core;

/// <summary>
/// Registers the Occt.NET native DLL directory before any Occt type is JITted.
/// The package's own loader is broken on .NET 8+ (Assembly.CodeBase is null),
/// so this must be called from a method that touches no Occt types.
/// </summary>
public static class NativeLoader
{
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetDefaultDllDirectories(uint directoryFlags);

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr AddDllDirectory(string newDirectory);

    private const uint LoadLibrarySearchDefaultDirs = 0x1000;
    private static int _initialised;

    public static void Configure()
    {
        if (Interlocked.Exchange(ref _initialised, 1) != 0) return;

        var nativeDir = Path.Combine(AppContext.BaseDirectory, "occt", "x64");
        if (!Directory.Exists(nativeDir))
            throw new DirectoryNotFoundException(
                $"OCCT natives not found at '{nativeDir}'. Is PlatformTarget x64 and Occt.NET referenced directly?");

        SetDefaultDllDirectories(LoadLibrarySearchDefaultDirs);
        AddDllDirectory(nativeDir);
    }
}
