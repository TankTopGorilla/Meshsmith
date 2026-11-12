using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using Meshsmith.App.Services;
using Xunit;

namespace Meshsmith.App.Tests;

public class DiagnosticBundleTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"diag-{Guid.NewGuid():N}");

    public DiagnosticBundleTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* a locked file must not fail a test */ }
    }

    private string WriteModel(int bytes = 32)
    {
        var path = Path.Combine(_dir, "part.stl");
        File.WriteAllBytes(path, Enumerable.Repeat((byte)'a', bytes).ToArray());
        return path;
    }

    private string WriteLog(int lines)
    {
        var path = Path.Combine(_dir, "meshsmith-app.log");
        File.WriteAllLines(path, Enumerable.Range(1, lines).Select(i => $"line {i}"));
        return path;
    }

    private string ZipPath => Path.Combine(_dir, "bundle.zip");

    private static string[] Entries(string zip) =>
        ZipFile.OpenRead(zip).Entries.Select(e => e.FullName.Replace('\\', '/')).ToArray();

    private static string Text(string zip, string entry)
    {
        using var archive = ZipFile.OpenRead(zip);
        using var reader = new StreamReader(archive.GetEntry(entry)!.Open());
        return reader.ReadToEnd();
    }

    private static DiagnosticInputs Inputs(string? model, string failure = "worker stopped (exit code -1073741819)") =>
        new(model, "CONVERSION FAILED", failure, "sewing", "0.1.3", new DateTime(2026, 11, 9, 23, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void Bundle_contains_the_failure_the_log_and_the_model()
    {
        var model = WriteModel();
        var expectedHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(model))).ToLowerInvariant();

        DiagnosticBundle.Create(ZipPath, Inputs(model), WriteLog(10));

        var entries = Entries(ZipPath);
        Assert.Contains("diagnostic.txt", entries);
        Assert.Contains("model.stl", entries);
        Assert.Contains("log-tail.txt", entries);

        var text = Text(ZipPath, "diagnostic.txt");
        Assert.Contains("exit code -1073741819", text);
        Assert.Contains("CONVERSION FAILED", text);
        Assert.Contains("sewing", text);
        Assert.Contains(expectedHash, text);
    }

    [Fact]
    public void A_model_over_the_cap_is_described_not_copied()
    {
        var model = WriteModel();
        var expectedHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(model))).ToLowerInvariant();

        DiagnosticBundle.Create(ZipPath, Inputs(model), WriteLog(3), maxModelBytes: 8);

        Assert.DoesNotContain("model.stl", Entries(ZipPath));
        var text = Text(ZipPath, "diagnostic.txt");
        Assert.Contains("model: not copied", text);
        Assert.Contains(expectedHash, text);
        Assert.Contains(model, text);
    }

    [Fact]
    public void Only_the_last_two_hundred_log_lines_are_kept()
    {
        DiagnosticBundle.Create(ZipPath, Inputs(null), WriteLog(500));

        var lines = Text(ZipPath, "log-tail.txt").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(200, lines.Length);
        Assert.Contains("line 500", lines[^1]);
        Assert.DoesNotContain("line 300", lines[0]);
    }

    [Fact]
    public void A_missing_log_or_model_does_not_throw()
    {
        var missing = Path.Combine(_dir, "nope.stl");

        DiagnosticBundle.Create(ZipPath, Inputs(missing), Path.Combine(_dir, "no.log"));

        var text = Text(ZipPath, "diagnostic.txt");
        Assert.Contains("model: not found", text);
        Assert.Contains("log: not found", text);
    }
}
