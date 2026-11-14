using Assimp;
using Meshsmith.Core.Geometry;
using Meshsmith.Core.Mesh;
using NMatrix = System.Numerics.Matrix4x4;
using NVector = System.Numerics.Vector3;

namespace Meshsmith.Core.Import;

/// <summary>FBX through AssimpNet (assimp 4.1.0).
///
/// <para>Assimp is used for FBX <b>only</b>: the same assimp build returns 0 meshes from a Blender
/// glTF 2.0 file (measured), which is why glTF goes through SharpGLTF instead.</para>
///
/// <para>Units are undecidable and the reader says so: Blender writes UnitScaleFactor = 1.0 whatever
/// the scene's unit settings are (five fixtures, measured), and assimp applies no scaling. So the
/// default takes the file's numbers as millimetres, the override says "these are metres - ×1000",
/// and every read carries a warning either way. This is the one place a wrong guess is only
/// recoverable by the user, so it is never silent.</para></summary>
public sealed class FbxMeshReader : IMeshReader
{
    public string Format => "FBX";

    public bool CanRead(string path, ReadOnlySpan<byte> head)
    {
        if (Path.GetExtension(path).Equals(".fbx", StringComparison.OrdinalIgnoreCase)) return true;

        ReadOnlySpan<byte> magic = "Kaydara FBX Binary"u8;
        return head.Length >= magic.Length && head[..magic.Length].SequenceEqual(magic);
    }

    public MeshReadResult Read(string path, MeshReadOptions options,
                               IProgress<double>? byteProgress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        byteProgress?.Report(0.0);

        string name = Path.GetFileName(path);
        Meshsmith.Core.NativeLoader.RequireAssimp();

        Scene scene;
        try
        {
            using var context = new AssimpContext();
            // Triangulate is not optional: without it a Blender cube comes back as six QUADS whose
            // PrimitiveType still claims Triangle (measured), and every face count downstream is wrong.
            scene = context.ImportFile(path, PostProcessSteps.Triangulate);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            throw new MeshFormatException(
                $"FBX support is not installed (assimp.dll could not be loaded: {ex.Message})", "assimp.dll missing");
        }
        catch (AssimpException ex)
        {
            throw new MeshFormatException($"'{name}' could not be read as FBX: {ex.Message}", "unreadable FBX");
        }

        if (scene is null)
            throw new MeshFormatException($"'{name}' could not be read as FBX", "unreadable FBX");

        bool asMillimetres = options.TreatNumbersAsMillimetres != false;
        double scale = asMillimetres ? 1.0 : 1000.0;

        var points = new List<Vec3>();
        int nodes = 0;

        void Walk(Node node, NMatrix parent)
        {
            token.ThrowIfCancellationRequested();
            nodes++;

            var world = parent * ToNumerics(node.Transform);

            foreach (int meshIndex in node.MeshIndices)
            {
                var mesh = scene.Meshes[meshIndex];
                foreach (var face in mesh.Faces)
                {
                    // Triangulate guarantees three corners; fans are still split rather than dropped,
                    // because a face the post-processor missed is geometry, not a reason to fail.
                    for (int i = 2; i < face.IndexCount; i++)
                    {
                        points.Add(Emit(mesh.Vertices[face.Indices[0]], world, scale));
                        points.Add(Emit(mesh.Vertices[face.Indices[i - 1]], world, scale));
                        points.Add(Emit(mesh.Vertices[face.Indices[i]], world, scale));
                    }
                }
            }

            foreach (var child in node.Children) Walk(child, world);
        }

        Walk(scene.RootNode, NMatrix.Identity);

        if (points.Count == 0)
            throw new MeshFormatException(
                $"'{name}' holds no triangle geometry ({nodes} node(s), {scene.MeshCount} mesh(es))",
                "no triangle geometry");

        byteProgress?.Report(1.0);
        return new MeshReadResult
        {
            Soup = new TriangleSoup { Points = points.ToArray() },
            Format = Format,
            UnitNote = asMillimetres
                ? "FBX · unit not declared · treated as millimetres"
                : "FBX · assumed metres · ×1000",
            AppliedScale = scale,
            Warnings =
            {
                asMillimetres
                    ? "FBX declares no usable unit (UnitScaleFactor=1.0); assuming millimetres — " +
                      "switch the unit setting if the part is 1000× off"
                    : "FBX declares no usable unit (UnitScaleFactor=1.0); taken as metres — ×1000 applied"
            }
        };
    }

    private static Vec3 Emit(Assimp.Vector3D vertex, NMatrix world, double scale)
    {
        var p = NVector.Transform(new NVector(vertex.X, vertex.Y, vertex.Z), world);
        return new Vec3(p.X * scale, p.Y * scale, p.Z * scale);
    }

    /// <summary>Assimp's matrices are row-major with column vectors; System.Numerics is the same
    /// matrix with row vectors, so a straight a1..d4 mapping and <c>parent * local</c> composes in the
    /// same order assimp's own traversal does.</summary>
    private static NMatrix ToNumerics(Assimp.Matrix4x4 m) => new(
        m.A1, m.A2, m.A3, m.A4,
        m.B1, m.B2, m.B3, m.B4,
        m.C1, m.C2, m.C3, m.C4,
        m.D1, m.D2, m.D3, m.D4);
}
