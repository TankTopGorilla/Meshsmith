using System.Numerics;
using System.Text;
using System.Text.Json;
using Meshsmith.Core.Geometry;
using Meshsmith.Core.Mesh;
using SharpGLTF.Schema2;
using SharpGLTF.Validation;

namespace Meshsmith.Core.Import;

/// <summary>glTF 2.0 and GLB through SharpGLTF.
///
/// <para>Units: glTF is metres by spec and the file declares nothing else (probe: no 'unit', 'scale',
/// 'meter' key in a Blender export). So the default is ×1000 - and the note says so, because a silent
/// 1000x shrink is invisible in a STEP file until somebody measures the part.</para>
///
/// <para>Assimp cannot do this: assimp 4.1.0 returns 0 meshes from a Blender glTF 2.0 file while
/// reporting the extension as supported. That is measured, not preferred.</para></summary>
public sealed class GltfMeshReader : IMeshReader
{
    public string Format => "glTF";

    public bool CanRead(string path, ReadOnlySpan<byte> head)
    {
        string extension = Path.GetExtension(path);
        if (extension.Equals(".glb", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".gltf", StringComparison.OrdinalIgnoreCase))
            return true;

        // Signature, for a glTF that was given some other name: "glTF" is the binary container's magic
        // and a JSON document is a .gltf by definition.
        if (head.Length >= 4 && head[0] == (byte)'g' && head[1] == (byte)'l'
                              && head[2] == (byte)'T' && head[3] == (byte)'F')
            return true;
        return head.Length > 0 && head[0] == (byte)'{';
    }

    public MeshReadResult Read(string path, MeshReadOptions options,
                               IProgress<double>? byteProgress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        byteProgress?.Report(0.0);

        string name = Path.GetFileName(path);
        ModelRoot model;
        try
        {
            model = ModelRoot.Load(path);
        }
        catch (LinkException ex)
        {
            // Draco/meshopt are extensionsRequired: SharpGLTF refuses the file before any mesh is
            // touched. Name what it asked for instead of leaking a schema exception to the UI.
            var required = RequiredExtensions(path);
            if (required.Count > 0)
                throw new MeshFormatException(
                    $"'{name}' uses {string.Join(", ", required)}, which Meshsmith cannot decode - " +
                    "re-export without it or install a decoder", required[0]);
            throw new MeshFormatException($"'{name}' is not a valid glTF/GLB: {ex.Message}", "malformed glTF");
        }

        var scene = model.DefaultScene ?? model.LogicalScenes.FirstOrDefault();
        if (scene is null)
            throw new MeshFormatException($"'{name}' holds no scene to read", "no scene");

        double scale = options.TreatNumbersAsMillimetres ? 1.0 : 1000.0;
        string note = options.TreatNumbersAsMillimetres
            ? "glTF · metres · ×1 (treated as mm)"
            : "glTF · metres · ×1000";

        var points = new List<Vec3>();
        var warnings = new List<string>();
        var skipped = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var meshes = new HashSet<SharpGLTF.Schema2.Mesh>();
        int meshNodes = 0;

        void Walk(Node node)
        {
            token.ThrowIfCancellationRequested();

            if (node.Mesh is { } mesh)
            {
                meshNodes++;
                meshes.Add(mesh);
                var world = node.WorldMatrix;      // includes every ancestor's transform

                foreach (var primitive in mesh.Primitives)
                {
                    if (!IsTriangleMode(primitive.DrawPrimitiveType))
                    {
                        string mode = primitive.DrawPrimitiveType.ToString();
                        skipped[mode] = skipped.TryGetValue(mode, out int seen) ? seen + 1 : 1;
                        continue;
                    }

                    var accessor = primitive.GetVertexAccessor("POSITION");
                    if (accessor is null)
                    {
                        warnings.Add($"'{node.Name}' has a triangle primitive without POSITION; skipped");
                        continue;
                    }

                    var positions = accessor.AsVector3Array();
                    // SharpGLTF expands STRIP and FAN and synthesises indices for non-indexed
                    // primitives, so this is the only triangulation path the reader needs.
                    foreach (var (a, b, c) in primitive.GetTriangleIndices())
                    {
                        points.Add(Emit(positions[a], world, scale));
                        points.Add(Emit(positions[b], world, scale));
                        points.Add(Emit(positions[c], world, scale));
                    }
                }
            }

            foreach (var child in node.VisualChildren) Walk(child);
        }

        foreach (var root in scene.VisualChildren) Walk(root);

        foreach (var (mode, count) in skipped)
            warnings.Add($"{count} {mode} primitive(s) were skipped: only triangles can become a solid");

        int instances = meshNodes - meshes.Count;
        if (instances > 0)
            warnings.Add($"the scene uses {meshes.Count} mesh(es) across {meshNodes} node instances; " +
                         $"every instance was expanded ({instances} extra)");

        if (points.Count == 0)
            throw new MeshFormatException(
                $"'{name}' holds no triangle geometry ({meshNodes} mesh node(s)" +
                (skipped.Count > 0 ? $"; skipped {string.Join(", ", skipped.Select(kv => $"{kv.Value} {kv.Key}"))}" : "") +
                ")", "no triangle geometry");

        byteProgress?.Report(1.0);
        return new MeshReadResult
        {
            Soup = new TriangleSoup { Points = points.ToArray() },
            Format = IsGlb(path) ? "GLB" : "glTF",
            UnitNote = note,
            AppliedScale = scale,
            Warnings = warnings,
            InstancesExpanded = instances
        };
    }

    private static Vec3 Emit(Vector3 position, Matrix4x4 world, double scale)
    {
        var transformed = Vector3.Transform(position, world);
        return new Vec3(transformed.X * scale, transformed.Y * scale, transformed.Z * scale);
    }

    private static bool IsTriangleMode(PrimitiveType mode) =>
        mode is PrimitiveType.TRIANGLES or PrimitiveType.TRIANGLE_STRIP or PrimitiveType.TRIANGLE_FAN;

    private static bool IsGlb(string path) =>
        Path.GetExtension(path).Equals(".glb", StringComparison.OrdinalIgnoreCase);

    /// <summary>extensionsRequired, read straight out of the container: ModelRoot.Load throws before it
    /// will hand back a model, so the only honest source is the file itself.</summary>
    private static List<string> RequiredExtensions(string path)
    {
        try
        {
            string json = ReadJson(path);
            if (json.Length == 0) return new List<string>();

            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("extensionsRequired", out var required) ||
                required.ValueKind != JsonValueKind.Array)
                return new List<string>();

            return required.EnumerateArray()
                           .Select(e => e.GetString())
                           .Where(s => !string.IsNullOrEmpty(s))
                           .Select(s => s!)
                           .ToList();
        }
        catch (Exception ex) when (ex is IOException or JsonException or ArgumentException)
        {
            return new List<string>();
        }
    }

    private static string ReadJson(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 12 && bytes[0] == (byte)'g' && bytes[1] == (byte)'l'
                               && bytes[2] == (byte)'T' && bytes[3] == (byte)'F')
        {
            // GLB: chunk 0 starts at byte 12 as (length, type); type 0x4E4F534A is "JSON".
            int length = BitConverter.ToInt32(bytes, 12);
            int type = BitConverter.ToInt32(bytes, 16);
            if (type != 0x4E4F534A || length < 0) return "";
            return Encoding.UTF8.GetString(bytes, 20, Math.Min(length, bytes.Length - 20));
        }
        return Encoding.UTF8.GetString(bytes);
    }
}
