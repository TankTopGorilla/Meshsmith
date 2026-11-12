using System.Text.Json.Serialization;

namespace Meshsmith.Core.Report;

public sealed class ConversionReport
{
    [JsonPropertyName("result_kind")] public string ResultKind { get; set; } = "prismatic";
    [JsonPropertyName("output_faces")] public int OutputFaces { get; set; }
    [JsonPropertyName("surface_types")] public Dictionary<string, int> SurfaceTypes { get; set; } = new();
    [JsonPropertyName("faceted_triangles")] public int FacetedTriangles { get; set; }
    [JsonPropertyName("fit_tolerance")] public double FitTolerance { get; set; }
    [JsonPropertyName("surface_deviation")] public double SurfaceDeviation { get; set; }
    [JsonPropertyName("warnings")] public List<string> Warnings { get; set; } = new();

    /// <summary>Verified / Mismatch / Skipped — what the written file was found to contain.</summary>
    [JsonPropertyName("artifact_status")] public string ArtifactStatus { get; set; } = "Skipped";
    [JsonPropertyName("artifact_detail")] public string ArtifactDetail { get; set; } = "";
    [JsonPropertyName("timings")] public Dictionary<string, double> Timings { get; set; } = new();
    [JsonPropertyName("repair")] public Dictionary<string, int> Repair { get; set; } = new();
    [JsonPropertyName("elapsed_seconds")] public double ElapsedSeconds { get; set; }
}
