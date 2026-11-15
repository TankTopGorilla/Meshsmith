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

    // R4: what a decimation erased, in fit tolerances. 0 when none ran - older report.json files stay
    // readable, the same rule the R3 surface-kind fields follow.
    [JsonPropertyName("decimate_from_triangles")] public int DecimateFromTriangles { get; set; }
    [JsonPropertyName("decimate_deviation")] public double DecimateDeviation { get; set; }
    [JsonPropertyName("recon_coverage")] public double ReconCoverage { get; set; }
    [JsonPropertyName("region_fallbacks")] public int RegionFallbacks { get; set; }

    /// <summary>Per-body sew ladders skipped because the analytic set could not close (E1-T5).</summary>
    [JsonPropertyName("skipped_ladders")] public int SkippedLadders { get; set; }
    [JsonPropertyName("warnings")] public List<string> Warnings { get; set; } = new();

    /// <summary>Verified / Mismatch / Skipped — what the written file was found to contain.</summary>
    [JsonPropertyName("artifact_status")] public string ArtifactStatus { get; set; } = "Skipped";
    [JsonPropertyName("artifact_detail")] public string ArtifactDetail { get; set; } = "";
    [JsonPropertyName("timings")] public Dictionary<string, double> Timings { get; set; } = new();
    [JsonPropertyName("repair")] public Dictionary<string, int> Repair { get; set; } = new();
    [JsonPropertyName("elapsed_seconds")] public double ElapsedSeconds { get; set; }
}
