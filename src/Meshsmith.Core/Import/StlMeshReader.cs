using Meshsmith.Core.Mesh;

namespace Meshsmith.Core.Import;

/// <summary>STL through the registry. This is a pure wrapper: it calls <see cref="StlReader.Read(string)"/>
/// and nothing else, so every STL conversion stays byte-identical to the pre-P10a pipeline - the
/// SHA-256 gate in the P10a plan was measured across this wrapper, not around it.
///
/// <para>Two deliberate limits. <c>CanRead</c> trusts the extension only: every STL starts with either
/// "solid" or an 80-byte header, and <c>StlReader.LooksBinary</c> already owns that decision - sniffing
/// twice invites the two to disagree. And the byte progress is coarse (0 then 1) because a per-byte
/// hook would have to live inside <c>StlReader</c>, which this phase must not change; P6 can move the
/// parameters inward once the gate has proven the wrapper.</para></summary>
public sealed class StlMeshReader : IMeshReader
{
    public string Format => "STL";

    public bool CanRead(string path, ReadOnlySpan<byte> head) =>
        Path.GetExtension(path).Equals(".stl", StringComparison.OrdinalIgnoreCase);

    public MeshReadResult Read(string path, MeshReadOptions options,
                               IProgress<double>? byteProgress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        byteProgress?.Report(0.0);

        var soup = StlReader.Read(path);

        byteProgress?.Report(1.0);
        return new MeshReadResult
        {
            Soup = soup,
            Format = Format,
            UnitNote = "STL · millimetres",
            AppliedScale = 1.0
        };
    }
}
