using Meshsmith.Core.Mesh;

namespace Meshsmith.Core.Import;

/// <summary>How a file's numbers should be read. There is no "guess the unit" switch: a format that
/// declares its unit is converted from what it declares, and a format that does not is treated as
/// millimetres unless the user says otherwise - loudly, in the result's warnings.</summary>
public sealed record MeshReadOptions
{
    /// <summary>Scene lengths are multiplied by this before they enter the pipeline.
    /// Default 1000 for metric scene formats; 1 when the user says "these numbers are mm".</summary>
    public double UnitToMillimetres { get; init; } = 1.0;

    /// <summary>The user's claim that the file's numbers are already millimetres. Formats whose unit
    /// is undecidable (FBX from Blender) convert only when this is false.</summary>
    public bool TreatNumbersAsMillimetres { get; init; }
}

/// <summary>What a reader recovered: the soup, and the facts that make the numbers auditable.</summary>
public sealed record MeshReadResult
{
    public required TriangleSoup Soup { get; init; }

    /// <summary>"STL" | "glTF" | "GLB" | "FBX".</summary>
    public required string Format { get; init; }

    /// <summary>One line the user can check: what the format declares, and what was done about it.</summary>
    public required string UnitNote { get; init; }

    /// <summary>The multiplier actually applied to the file's numbers. 1.0 means "taken as mm".</summary>
    public required double AppliedScale { get; init; }

    /// <summary>Anything the file forced the reader to skip or assume. Never silent.</summary>
    public List<string> Warnings { get; init; } = new();

    /// <summary>Nodes with a mesh beyond the first use of each mesh: > 0 means a mesh was expanded
    /// more than once (an instance is a union, not a set).</summary>
    public int InstancesExpanded { get; init; }
}

/// <summary>A file no reader can honestly open. <see cref="Feature"/> names the reason in one token
/// ("no triangle geometry", "KHR_draco_mesh_compression", "unrecognised format") so the caller can
/// branch without matching prose.</summary>
public sealed class MeshFormatException(string message, string feature = "") : Exception(message)
{
    public string Feature { get; } = feature;
}

/// <summary>The one place that decides which reader opens a file. Ordered, first match wins: the
/// order is the order a file is asked "are you mine", so STL stays first because it is what every
/// existing project already is.</summary>
public static class MeshReaderRegistry
{
    private const int HeadBytes = 512;

    private static readonly IMeshReader[] DefaultReaders =
    {
        new StlMeshReader()
    };

    public static IReadOnlyList<IMeshReader> Readers => DefaultReaders;

    public static MeshReadResult Read(string path, MeshReadOptions? options = null,
                                      IProgress<double>? byteProgress = null,
                                      CancellationToken token = default)
        => Read(path, DefaultReaders, options ?? new MeshReadOptions(), byteProgress, token);

    /// <summary>The injectable form: the pipeline takes its readers as a list so a test can prove the
    /// call actually goes through this seam instead of the static reader.</summary>
    public static MeshReadResult Read(string path, IReadOnlyList<IMeshReader> readers,
                                      MeshReadOptions options,
                                      IProgress<double>? byteProgress = null,
                                      CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(readers);

        // Before the file is opened, not after: a cancelled read must not touch the disk at all.
        token.ThrowIfCancellationRequested();

        // First pass with no signature at all: a reader that identifies on extension alone opens the
        // file itself, so a stream (the worker can be handed a named pipe) is never read twice and an
        // STL costs exactly the one read it always did.
        foreach (var reader in readers)
        {
            if (reader.CanRead(path, ReadOnlySpan<byte>.Empty))
                return reader.Read(path, options, byteProgress, token);
        }

        // Second pass: an unfamiliar extension, so the signature has to decide.
        byte[] head = ReadHead(path);
        foreach (var reader in readers)
        {
            if (reader.CanRead(path, head))
                return reader.Read(path, options, byteProgress, token);
        }

        string extension = Path.GetExtension(path);
        throw new MeshFormatException(
            $"no reader recognises '{Path.GetFileName(path)}': extension '{extension}' and the first " +
            $"{head.Length} bytes match none of {string.Join(", ", readers.Select(r => r.Format))}",
            "unrecognised format");
    }

    private static byte[] ReadHead(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        // No stream.Length: a named pipe (the worker reads STL from one) is not seekable and throws
        // NotSupportedException there. Read up to HeadBytes and stop at EOF - which for a pipe is the
        // same "block until the writer is done" the reader itself does, so the two cannot disagree.
        var buffer = new byte[HeadBytes];
        int length = 0;
        while (length < buffer.Length)
        {
            int read = stream.Read(buffer, length, buffer.Length - length);
            if (read <= 0) break;
            length += read;
        }
        return length == buffer.Length ? buffer : buffer[..length];
    }
}
