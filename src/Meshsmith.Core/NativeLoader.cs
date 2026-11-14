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

    /// <summary>Where the FBX native payload is expected. Null means "beside the app or under the
    /// package's runtimes/win-x64/native"; a test sets it to a bogus path to prove the failure stays
    /// a named <c>MeshFormatException</c> instead of a DllNotFoundException out of the marshaller.</summary>
    public static string? AssimpPath { get; set; }

    public static string ResolveAssimpPath()
    {
        if (!string.IsNullOrEmpty(AssimpPath)) return AssimpPath!;

        string beside = Path.Combine(AppContext.BaseDirectory, "assimp.dll");
        if (File.Exists(beside)) return beside;

        return Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", "assimp.dll");
    }

    /// <summary>Call before touching an Assimp type: assimp.dll is the one native dependency of the
    /// FBX reader, and a missing one is a named failure the UI can show, not a crash.</summary>
    public static void RequireAssimp()
    {
        string path = ResolveAssimpPath();
        if (!File.Exists(path))
            throw new Import.MeshFormatException(
                $"FBX support is not installed (assimp.dll missing at '{path}')", "assimp.dll missing");
    }
}
