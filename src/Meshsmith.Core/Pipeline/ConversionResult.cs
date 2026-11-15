using Meshsmith.Core.Export;
using Meshsmith.Core.Mesh;

namespace Meshsmith.Core.Pipeline;

public sealed class ConversionResult
{
    public required string StepPath { get; init; }
    public required string IgesPath { get; init; }
    public required string FacesPath { get; init; }
    public required int FaceCount { get; init; }
    public required int SolidCount { get; init; }
    public required bool IsValidSolid { get; init; }
    public required double Volume { get; init; }
    public required double SurfaceDeviation { get; init; }
    public required double FitTolerance { get; init; }
    public required double ElapsedSeconds { get; init; }
    public required int FacetedTriangles { get; init; }
    public required int Edges { get; init; }
    public required int TriangleCount { get; init; }
    public required string Kind { get; init; }

    /// <summary>Share of triangles the classifier turned into analytic surfaces (0..1).</summary>
    public double ReconCoverage { get; init; }

    /// <summary>Regions that fell back to facets for a reason of their own.</summary>
    public int RegionFallbacks { get; init; }

    /// <summary>Per-body sew ladders not run because the analytic set provably could not close
    /// (E1-T5). Defaulted, so a result read back from an older file still deserialises.</summary>
    public int SkippedLadders { get; init; }

    /// <summary>What the written STEP contains, read back from disk.</summary>
    public required ArtifactCheck Artifact { get; init; }

    /// <summary>Seconds per pipeline stage, measured - not the progress percentages, which are
    /// effort guesses. "total" is the sum the stopwatch saw.</summary>
    public IReadOnlyDictionary<string, double> Timings { get; init; } = new Dictionary<string, double>();

    /// <summary>What the repair stage changed before anything was fitted.</summary>
    public RepairReport Repair { get; init; } = RepairReport.None;

    /// <summary>The triangle count the mesh came in with when a decimation ran, else 0 (R4).</summary>
    public int DecimateFromTriangles { get; init; }

    /// <summary>Farthest distance the decimation moved the surface, in fit tolerances (R4). 0 when no
    /// decimation ran. This is the number that answers "did we erase detail".</summary>
    public double DecimateDeviation { get; init; }

    /// <summary>The format the mesh actually came in as: "STL" | "glTF" | "GLB" | "FBX" (P10a).
    /// Defaulted so a faces.json written before P10a still deserialises.</summary>
    public string SourceFormat { get; init; } = "";

    /// <summary>One line saying what the file's numbers meant and what was done about it, e.g.
    /// "glTF · metres · ×1000" (P10a).</summary>
    public string UnitNote { get; init; } = "";

    /// <summary>The multiplier the reader applied to the file's numbers. 1 means "taken as mm", and
    /// that is also the sensible default for a pre-P10a result (P10a).</summary>
    public double AppliedScale { get; init; } = 1.0;

    /// <summary>What the reader had to skip or assume. Kept apart from <see cref="Warnings"/> because
    /// these are about the input file, not about what the converter did to it (P10a).</summary>
    public List<string> ImportWarnings { get; init; } = new();

    public List<string> Warnings { get; init; } = new();
}
