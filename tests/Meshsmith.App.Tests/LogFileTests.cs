using System.IO;
using System.Text;
using Meshsmith.App.Services;
using Xunit;

namespace Meshsmith.App.Tests;

/// <summary>The log used to be deleted the moment it passed 2 MiB, and only at startup, so the
/// previous session's evidence was gone exactly when a bug report needed it. Two files now, and the
/// rotation happens on the write.</summary>
public class LogFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"log-{Guid.NewGuid():N}");

    public LogFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* a held handle must not fail a test */ }
    }

    [Fact]
    public void A_log_over_two_megabytes_rotates_into_exactly_two_files()
    {
        var log = new LogFile(_dir, limitBytes: 2 * 1024 * 1024);
        var old = new byte[2 * 1024 * 1024 + 1];
        for (int i = 0; i < old.Length; i++) old[i] = (byte)'a';
        File.WriteAllBytes(log.Path, old);

        log.Start();        // the previous session's log is over the limit, so it becomes .1

        Assert.True(File.Exists(log.Path), "no live log after rotation");
        Assert.True(File.Exists(log.PreviousPath), "the previous log was lost");
        Assert.Equal(2, Directory.GetFiles(_dir).Length);        // exactly two files, always

        Assert.Equal(old.Length, new FileInfo(log.PreviousPath).Length);   // the old bytes went to .1
        Assert.Contains("=== start", File.ReadAllText(log.Path));          // and the new one began
    }

    [Fact]
    public void A_log_below_the_limit_is_not_rotated()
    {
        var log = new LogFile(_dir);
        var kept = "the line that must survive\n";
        File.WriteAllText(log.Path, kept, new UTF8Encoding(false));

        log.Start();

        Assert.False(File.Exists(log.PreviousPath));
        var text = File.ReadAllText(log.Path);
        Assert.StartsWith(kept, text);          // appended to, not replaced
        Assert.Contains("=== start", text);
        Assert.Single(Directory.GetFiles(_dir));
    }
}
