using Meshsmith.Core.Fit;
using Meshsmith.Core.Mesh;

namespace Meshsmith.Core.Pipeline;

/// <summary>
/// How much of the mesh the classifier turned into analytic surfaces. A triangle share, not an area
/// share: it is the number the recognition program quotes (clamp 33.6 %, Part 1 94.2 %), and it has to
/// stay comparable with those measurements.
/// </summary>
public sealed record RecognitionStats(int TotalTriangles, int AnalyticTriangles, double Coverage)
{
    public static RecognitionStats Compute(MeshData mesh, IReadOnlyList<ClassifiedRegion> regions)
    {
        int total = mesh.TriangleCount;
        int analytic = regions.Where(r => r.Primitive is not FacetedPrimitive).Sum(r => r.Triangles.Length);
        return new RecognitionStats(total, analytic, total == 0 ? 0 : (double)analytic / total);
    }
}
