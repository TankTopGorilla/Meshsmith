using Meshsmith.Core.Mesh;

namespace Meshsmith.Core.Pipeline;

/// <summary>
/// Whether a body's analytic face set can possibly sew closed, from the mesh, without sewing (:251-253 sews analytic
/// regions only, so its faces pair only on an edge with an analytic triangle on both sides; zero such edges ⇒ every
/// boundary edge unpaired at any tolerance). NECESSARY, not sufficient — an open plate is true.
/// </summary>
public static class AnalyticSeam
{
    public static bool CanClose(MeshTopology topo, int[] triangleRegion, bool[] analytic,
                                IReadOnlyList<int> component)
    {
        if (topo.Neighbor.Length != triangleRegion.Length * 3)
            throw new ArgumentException("topology and region map disagree on the triangle count");
        if (component.Count == 0) return false;

        var inComponent = new bool[analytic.Length];
        foreach (int r in component) inComponent[r] = true;

        for (int t = 0; t < triangleRegion.Length; t++)
        {
            int r = triangleRegion[t];
            if (r < 0 || !inComponent[r] || !analytic[r]) continue;
            for (int e = 0; e < 3; e++)
            {
                int nb = topo.Neighbor[t * 3 + e];
                if (nb < 0) continue;
                int rn = triangleRegion[nb];
                if (rn >= 0 && inComponent[rn] && analytic[rn]) return true;   // one seam is enough
            }
        }
        return false;
    }
}
