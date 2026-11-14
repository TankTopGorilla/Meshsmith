using Meshsmith.Core.Mesh;

namespace Meshsmith.Core.Import;

/// <summary>STL through the registry. <c>CanRead</c> trusts the extension only: every STL starts
/// with either "solid" or an 80-byte header, and <c>StlReader.LooksBinary</c> already owns that
/// decision - sniffing twice invites the two to disagree.
///
/// <para>The token and the byte fraction go all the way in: <see cref="StlReader"/> checks the
/// token every chunk and reports the bytes it has consumed, so a cancel lands inside the read
/// instead of after it, and the bar moves while a large file is parsed. The one part that stays
/// uninterruptible is <c>File.ReadAllBytes</c>, which is a single kernel call.</para></summary>
public sealed class StlMeshReader : IMeshReader
{
    public string Format => "STL";

    public bool CanRead(string path, ReadOnlySpan<byte> head) =>
        Path.GetExtension(path).Equals(".stl", StringComparison.OrdinalIgnoreCase);

    public MeshReadResult Read(string path, MeshReadOptions options,
                               IProgress<double>? byteProgress, CancellationToken token)
    {
        byteProgress?.Report(0.0);

        var soup = StlReader.Read(path, byteProgress, token);

        return new MeshReadResult
        {
            Soup = soup,
            Format = Format,
            UnitNote = "STL · millimetres",
            AppliedScale = 1.0
        };
    }
}
