using Meshsmith.Core.Geometry;
using Meshsmith.Core.Mesh;

namespace Meshsmith.Core.Seg;

public static class RegionGrower
{
    public static List<int[]> Grow(MeshData mesh, MeshTopology topology, double dihedralDeg) =>
        Grow(mesh, topology, dihedralDeg, null);

    /// <summary>
    /// The same flood fill, restricted to <paramref name="allowed"/> (null means every triangle). The
    /// classifier's second pass re-grows only the residual and the regions that touch it: a grow that
    /// could leave that set would re-claim triangles another region already owns, and the pass's
    /// "how many analytic triangles does this piece take over" count would stop being honest.
    /// </summary>
    public static List<int[]> Grow(MeshData mesh, MeshTopology topology, double dihedralDeg,
                                   bool[]? allowed)
    {
        double cos = Math.Cos(dihedralDeg * Math.PI / 180.0);
        var region = Enumerable.Repeat(-1, mesh.TriangleCount).ToArray();
        var regions = new List<int[]>();
        var stack = new Stack<int>();

        for (int seed = 0; seed < mesh.TriangleCount; seed++)
        {
            if (region[seed] >= 0) continue;
            if (allowed is not null && !allowed[seed]) continue;
            int id = regions.Count;
            var members = new List<int>();
            region[seed] = id;
            stack.Push(seed);

            while (stack.Count > 0)
            {
                int t = stack.Pop();
                members.Add(t);
                var nt = mesh.Normal(t);
                for (int e = 0; e < 3; e++)
                {
                    int nb = topology.Neighbor[t * 3 + e];
                    if (nb < 0 || region[nb] >= 0) continue;
                    if (allowed is not null && !allowed[nb]) continue;
                    if (Vec3.Dot(nt, mesh.Normal(nb)) < cos) continue;
                    region[nb] = id;
                    stack.Push(nb);
                }
            }
            regions.Add(members.ToArray());
        }
        return regions;
    }
}
