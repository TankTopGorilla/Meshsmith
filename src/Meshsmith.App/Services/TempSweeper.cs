using System.IO;

namespace Meshsmith.App.Services;

/// <summary>Conversions write into <c>%TEMP%\meshsmith-&lt;guid&gt;</c>. A cancelled run now removes its
/// own directory, but a crash, a kill or an older build leaves them behind for good. The sweep at
/// startup deletes the ones old enough that no running instance can still own them.</summary>
public static class TempSweeper
{
    public const string Prefix = "meshsmith-";

    /// <param name="olderThan">Age below which a directory is left alone — a running conversion's
    /// directory is seconds old, so a day of slack is generous.</param>
    /// <param name="root">Defaults to the system temp directory; tests pass their own.</param>
    /// <returns>How many directories were removed.</returns>
    public static int Sweep(TimeSpan olderThan, string? root = null)
    {
        var directory = root ?? Path.GetTempPath();
        if (!Directory.Exists(directory)) return 0;

        var removed = 0;
        IEnumerable<string> candidates;
        try
        {
            candidates = Directory.EnumerateDirectories(directory, Prefix + "*");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }

        foreach (var path in candidates)
        {
            try
            {
                if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(path) < olderThan) continue;

                Directory.Delete(path, recursive: true);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // In use, or not ours to delete: the next start will try again.
            }
        }

        return removed;
    }
}
