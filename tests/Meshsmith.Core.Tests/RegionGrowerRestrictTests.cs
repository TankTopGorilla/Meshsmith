using Meshsmith.Core.Mesh;
using Meshsmith.Core.Seg;
using Xunit;

namespace Meshsmith.Core.Tests;

/// <summary>
/// The second-chance pass re-grows only the residual, so the flood fill needs a set to stay inside.
/// A grow that can leave the set would re-claim triangles another region already owns, and the pass's
/// "how many analytic triangles does this piece take over" count would be a lie.
/// </summary>
public class RegionGrowerRestrictTests
{
    private static MeshData Cube(double s = 10) => new()
    {
        Vertices =
        [
            new(0,0,0), new(s,0,0), new(s,s,0), new(0,s,0),
            new(0,0,s), new(s,0,s), new(s,s,s), new(0,s,s),
        ],
        Indices =
        [
            0,3,2, 0,2,1,  4,5,6, 4,6,7,  0,1,5, 0,5,4,
            1,2,6, 1,6,5,  2,3,7, 2,7,6,  3,0,4, 3,4,7,
        ],
    };

    [Fact]
    public void A_restricted_grow_stays_inside_the_set()
    {
        var mesh = Cube();
        var topo = MeshTopology.Build(mesh);

        var all = RegionGrower.Grow(mesh, topo, 12);
        Assert.Equal(6, all.Count);                       // six faces, two triangles each

        // allow only two of the six faces: the grow must find exactly those two regions and
        // claim no triangle from the other four
        var allowed = new bool[mesh.TriangleCount];
        for (int t = 0; t < 4; t++) allowed[t] = true;     // the bottom face and the top face

        var restricted = RegionGrower.Grow(mesh, topo, 12, allowed);
        Assert.Equal(2, restricted.Count);
        Assert.Equal(4, restricted.Sum(r => r.Length));
        Assert.All(restricted, r => Assert.All(r, t => Assert.True(t < 4)));
    }

    [Fact]
    public void The_unrestricted_grow_is_unchanged()
    {
        var mesh = Cube();
        var topo = MeshTopology.Build(mesh);

        var viaOverload = RegionGrower.Grow(mesh, topo, 12, null);
        Assert.Equal(6, viaOverload.Count);
        Assert.Equal(mesh.TriangleCount, viaOverload.Sum(r => r.Length));
    }
}
