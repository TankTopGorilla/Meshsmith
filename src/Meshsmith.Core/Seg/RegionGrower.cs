using Meshsmith.Core.Geometry;
using Meshsmith.Core.Mesh;

namespace Meshsmith.Core.Seg;

public static class RegionGrower
{
    public static List<int[]> Grow(MeshData mesh, MeshTopology topology, double dihedralDeg)
    {
        double cos = Math.Cos(dihedralDeg * Math.PI / 180.0);
        var region = Enumerable.Repeat(-1, mesh.TriangleCount).ToArray();
        var regions = new List<int[]>();
        var stack = new Stack<int>();

        for (int seed = 0; seed < mesh.TriangleCount; seed++)
        {
            if (region[seed] >= 0) continue;
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
