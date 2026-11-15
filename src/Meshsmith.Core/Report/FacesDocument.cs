using System.Text.Json.Serialization;

namespace Meshsmith.Core.Report;

public sealed class FacesDocument
{
    [JsonPropertyName("schema")] public int Schema { get; set; } = 1;
    [JsonPropertyName("kind")] public string Kind { get; set; } = "prismatic";
    [JsonPropertyName("tolerance")] public double Tolerance { get; set; }
    [JsonPropertyName("faces")] public List<FaceRecord> Faces { get; set; } = new();
    [JsonPropertyName("triangle_face")] public int[] TriangleFace { get; set; } = Array.Empty<int>();
    [JsonPropertyName("facet_regions")] public List<FacetRegion> FacetRegions { get; set; } = new();
    [JsonPropertyName("summary")] public Summary Summary { get; set; } = new();
}

public sealed class FaceRecord
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("category")] public string Category { get; set; } = "plane";
    [JsonPropertyName("surface")] public Dictionary<string, object?> Surface { get; set; } = new();
    [JsonPropertyName("area")] public double Area { get; set; }
    [JsonPropertyName("center")] public double[] Center { get; set; } = Array.Empty<double>();
    [JsonPropertyName("normal")] public double[] Normal { get; set; } = Array.Empty<double>();
    [JsonPropertyName("neighbors")] public int[] Neighbors { get; set; } = Array.Empty<int>();
    [JsonPropertyName("source_triangles")] public int SourceTriangles { get; set; }
    [JsonPropertyName("fit")] public FitRecord Fit { get; set; } = new();
}

public sealed class FitRecord
{
    [JsonPropertyName("max")] public double Max { get; set; }
    [JsonPropertyName("mean")] public double Mean { get; set; }
}

public sealed class FacetRegion
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("faces")] public int[] Faces { get; set; } = Array.Empty<int>();
    [JsonPropertyName("triangles")] public int Triangles { get; set; }
    [JsonPropertyName("area")] public double Area { get; set; }
    [JsonPropertyName("center")] public double[] Center { get; set; } = Array.Empty<double>();
    [JsonPropertyName("bounds")] public double[] Bounds { get; set; } = Array.Empty<double>();
    [JsonPropertyName("normal_spread_degrees")] public double NormalSpreadDegrees { get; set; }
    [JsonPropertyName("around")] public string[] Around { get; set; } = Array.Empty<string>();
}

public sealed class Summary
{
    [JsonPropertyName("faces")] public int Faces { get; set; }
    [JsonPropertyName("triangles")] public int Triangles { get; set; }
    [JsonPropertyName("planes")] public int Planes { get; set; }
    [JsonPropertyName("cylinders")] public int Cylinders { get; set; }
    [JsonPropertyName("cones")] public int Cones { get; set; }
    // R3: the two new surface kinds. int defaults keep every older faces.json valid - the App's
    // TempFiles.FacesJson and every recorded fixture predate them.
    [JsonPropertyName("toruses")] public int Toruses { get; set; }
    [JsonPropertyName("spheres")] public int Spheres { get; set; }
    [JsonPropertyName("faceted_triangles")] public int FacetedTriangles { get; set; }
    [JsonPropertyName("recon_coverage")] public double ReconCoverage { get; set; }
    [JsonPropertyName("region_fallbacks")] public int RegionFallbacks { get; set; }
    // E1-T5: per-body sew ladders skipped because the analytic set could not close. int default
    // keeps every older faces.json valid, the R3/R4 rule.
    [JsonPropertyName("skipped_ladders")] public int SkippedLadders { get; set; }
    [JsonPropertyName("edges")] public int Edges { get; set; }

    // R4: present only when a decimation ran, 0 otherwise - the R3 rule, so every faces.json written
    // before decimation existed still deserialises.
    [JsonPropertyName("decimate_from_triangles")] public int DecimateFromTriangles { get; set; }
    [JsonPropertyName("decimate_deviation")] public double DecimateDeviation { get; set; }
}
