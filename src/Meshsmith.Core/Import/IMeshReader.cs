namespace Meshsmith.Core.Import;

/// <summary>One mesh container format. A reader owns two decisions the pipeline must never second
/// guess: how the file's triangles are recovered, and what its numbers mean. Both are reported back
/// in <see cref="MeshReadResult"/> rather than applied silently, because a 1000x unit error is
/// invisible in a STEP file until somebody measures the part.</summary>
public interface IMeshReader
{
    /// <summary>What the file was, as printed to the user: "STL", "glTF", "GLB", "FBX".</summary>
    string Format { get; }

    /// <summary>Extension <b>and</b> signature. The head is up to 512 bytes of the file; an
    /// <b>empty</b> head means the registry is asking on extension alone, which is how a stream input
    /// (a named pipe has no seekable length) avoids being read twice. A reader that trusts extensions
    /// says so by returning true for the empty head.</summary>
    bool CanRead(string path, ReadOnlySpan<byte> head);

    /// <summary>Read the whole file into one triangle soup in millimetres.
    /// <paramref name="byteProgress"/> is 0..1 over bytes consumed where the format allows it, and
    /// coarse otherwise; <paramref name="token"/> must be honoured before any parsing starts.</summary>
    MeshReadResult Read(string path, MeshReadOptions options,
                        IProgress<double>? byteProgress, CancellationToken token);
}
