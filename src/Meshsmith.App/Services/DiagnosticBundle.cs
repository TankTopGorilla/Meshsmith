using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Meshsmith.App.Services;

/// <summary>Everything a bundle needs, gathered at the moment of failure.</summary>
public sealed record DiagnosticInputs(
    string? SourcePath,
    string FailureTitle,
    string FailureText,
    string Stage,
    string AppVersion,
    DateTime TimestampUtc);

/// <summary>
/// Writes the file that makes a bug report reproducible: the failure text (which already carries the
/// worker exit code and its output), the model if it is small enough to be useful, and the tail of
/// the log. It never sends anything anywhere - the bundle is a local file the user chooses to share.
/// </summary>
public static class DiagnosticBundle
{
    public const long DefaultMaxModelBytes = 100L * 1024 * 1024;
    private const int LogTailLines = 200;

    public static void Create(string zipPath, DiagnosticInputs inputs, string? logPath,
                              long maxModelBytes = DefaultMaxModelBytes)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(zipPath));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var modelInfo = inputs.SourcePath is { } candidate ? new FileInfo(candidate) : null;
        bool modelExists = modelInfo?.Exists == true;
        bool copyModel = modelExists && modelInfo!.Length <= maxModelBytes;
        string? hash = modelExists ? HashOf(inputs.SourcePath) : null;
        bool logExists = logPath is not null && File.Exists(logPath);

        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        Write(archive, "diagnostic.txt", Describe(inputs, hash, maxModelBytes, logExists, copyModel));
        Write(archive, "log-tail.txt", Tail(logPath));

        if (copyModel)
            archive.CreateEntryFromFile(inputs.SourcePath!, "model.stl", CompressionLevel.Optimal);
    }

    private static string Describe(DiagnosticInputs inputs, string? hash, long maxModelBytes,
                                   bool logExists, bool copyModel)
    {
        var text = new StringBuilder();
        text.AppendLine("meshsmith diagnostic bundle");
        text.AppendLine($"generated: {inputs.TimestampUtc:yyyy-MM-dd HH:mm:ss} UTC");
        text.AppendLine($"app: {inputs.AppVersion}");
        text.AppendLine($"os: {RuntimeInformation.OSDescription}");
        text.AppendLine($"runtime: {RuntimeInformation.FrameworkDescription}");
        text.AppendLine($"culture: {System.Globalization.CultureInfo.CurrentCulture.Name}");
        text.AppendLine($"stage: {inputs.Stage}");
        text.AppendLine();
        text.AppendLine($"failure: {inputs.FailureTitle}");
        text.AppendLine(inputs.FailureText);
        text.AppendLine();

        if (inputs.SourcePath is not { } model)
        {
            text.AppendLine("source: none");
            text.AppendLine("model: not found");
        }
        else
        {
            text.AppendLine($"source: {model}");
            var info = new FileInfo(model);
            if (!info.Exists)
            {
                text.AppendLine("model: not found");
            }
            else
            {
                text.AppendLine($"size: {info.Length} bytes");
                text.AppendLine($"modified: {info.LastWriteTimeUtc:yyyy-MM-dd HH:mm:ss} UTC");
                text.AppendLine($"sha256: {hash ?? "(not hashed: over the size cap)"}");
                text.AppendLine(copyModel
                    ? "model: included"
                    : $"model: not copied (over the {maxModelBytes}-byte cap)");
            }
        }

        text.AppendLine(logExists ? $"log: included (last {LogTailLines} lines)" : "log: not found");
        return text.ToString();
    }

    /// <summary>Reads the tail without loading a log that may be megabytes long.</summary>
    private static string Tail(string? logPath)
    {
        if (logPath is null || !File.Exists(logPath)) return "";
        try
        {
            var lines = File.ReadLines(logPath).TakeLast(LogTailLines);
            return string.Join("\n", lines) + "\n";
        }
        catch (IOException)
        {
            return "";
        }
    }

    private static void Write(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }

    /// <summary>
    /// Always hashed, even when the model is too big to copy: the hash is what identifies the exact
    /// file the user can then send by other means. The caller runs <see cref="Create"/> off the UI
    /// thread, so a large read never freezes the window.
    /// </summary>
    private static string? HashOf(string? path)
    {
        if (path is null) return null;
        var info = new FileInfo(path);
        if (!info.Exists) return null;

        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        catch (IOException)
        {
            return null;
        }
    }
}
