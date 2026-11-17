using Meshsmith.Core.Mesh;
using Meshsmith.Core.Seg;
using Meshsmith.Core.Tests.Fixtures;
using Xunit;

namespace Meshsmith.Core.Tests;

/// <summary>
/// The centroid grid is the hypothesis segmenter's membership index. Its contract is small but every
/// part of it is load-bearing: the ring block is what makes local fitting possible at all (a curved
/// fit needs more than 3 points), and the cull's slack is derived from CellRadius, so a cell that
/// reports the wrong radius silently drops real inliers.
/// </summary>
public class CentroidGridTests
{
    [Fact]
    public void CentroidGrid_Collects_Exactly_The_Cells_In_Ring()
    {
        // A 10x10 grid of quads on a plane: 100 cells, one triangle pair per quad, so 200 triangles
        // spread evenly over the grid's x-y. Divisions tuned so the grid is comfortably 3D-occupied.
        var mesh = SyntheticMeshes.Plane(size: 10, nx: 10, ny: 10);
        var grid = new CentroidGrid(mesh, divisions: 8);

        // ---- ring 0 on an occupied cell is exactly that cell's triangles, ascending
        int occupied = grid.OccupiedCells[grid.OccupiedCells.Count / 2];
        var ring0 = new List<int>();
        grid.Collect(occupied, 0, ring0);

        var expected = grid.Cell(occupied)!;
        Assert.Equal(expected.Count, ring0.Count);
        Assert.Equal(expected.ToArray(), ring0.ToArray());          // same ids, same ascending order
        Assert.Equal(ring0.OrderBy(t => t).ToArray(), ring0.ToArray());  // explicitly ascending
        Assert.Equal(ring0.Distinct().Count(), ring0.Count);        // no duplicates

        // ---- ring 1 is the 27-cell block: every occupied cell within one step contributes its
        //      members exactly once, and the total is the sum of those cells (no double counting)
        var ring1 = new List<int>();
        grid.Collect(occupied, 1, ring1);

        var blockCells = BlockCells(grid, occupied, 1);
        int blockSum = blockCells.Sum(c => grid.Cell(c)?.Count ?? 0);
        Assert.Equal(blockSum, ring1.Count);
        Assert.Equal(ring1.Distinct().Count(), ring1.Count);

        // ring 1 contains ring 0 (a block includes its own cell) and is strictly larger here
        Assert.All(ring0, t => Assert.Contains(t, ring1));
        Assert.True(ring1.Count > ring0.Count);

        // ---- a degenerate mesh: every triangle at the same place is ONE occupied cell, no exception
        var degenerate = Collapsed(64);
        var collapsedGrid = new CentroidGrid(degenerate, divisions: 8);
        Assert.Single(collapsedGrid.OccupiedCells);

        var all = new List<int>();
        collapsedGrid.Collect(collapsedGrid.OccupiedCells[0], 2, all);
        Assert.Equal(64, all.Count);
        Assert.Equal(all.Distinct().Count(), all.Count);
    }

    /// <summary>
    /// The cell test the scoring loop relies on is only sound if CellRadius really is the maximum
    /// distance from a cell's centre to a point inside it. Assert it against the grid's own geometry:
    /// every centroid must lie within CellRadius of ITS cell's centre.
    /// </summary>
    [Fact]
    public void Every_Centroid_Is_Within_CellRadius_Of_Its_Cell_Centre()
    {
        var mesh = SyntheticMeshes.Plane(size: 25, nx: 30, ny: 30);
        var grid = new CentroidGrid(mesh, divisions: 12);

        double worst = 0;
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            var c = mesh.Centroid(t);
            worst = Math.Max(worst, (c - grid.CellCentre(grid.CellOf(c))).Length);
        }
        Assert.True(worst <= grid.CellRadius + 1e-12,
            $"worst centroid offset {worst} exceeds CellRadius {grid.CellRadius}");
        // ...and the bound is not vacuously loose: a grid this size lives on the same scale.
        Assert.True(worst > 0.9 * grid.CellRadius || grid.CellSize < 1e-6,
            $"CellRadius {grid.CellRadius} is not a real bound (worst {worst})");
    }

    // ---- helpers ----------------------------------------------------------------------

    /// <summary>The same (2r+1)^3 neighbourhood the grid defines, computed here from cell ids alone
    /// so the assertion does not restate the implementation.</summary>
    private static List<int> BlockCells(CentroidGrid grid, int cellId, int ring)
    {
        // The grid's own cells are the source of truth for coordinates: walk every occupied cell and
        // keep the ones whose centre is within `ring` cells in each axis of the seed's centre.
        var seed = grid.CellCentre(cellId);
        double step = grid.CellSize;
        var result = new List<int>();
        foreach (int id in grid.OccupiedCells)
        {
            var p = grid.CellCentre(id);
            double dx = Math.Abs(p.X - seed.X), dy = Math.Abs(p.Y - seed.Y), dz = Math.Abs(p.Z - seed.Z);
            if (dx <= ring * step + 1e-9 && dy <= ring * step + 1e-9 && dz <= ring * step + 1e-9)
                result.Add(id);
        }
        return result;
    }

    /// <summary>64 triangles sharing one centroid: the grid must not divide by a zero extent.</summary>
    private static MeshData Collapsed(int n)
    {
        var v = new List<Geometry.Vec3>();
        var idx = new List<int>();
        for (int i = 0; i < n; i++)
        {
            int b = v.Count;
            v.Add(new Geometry.Vec3(1, 2, 3));
            v.Add(new Geometry.Vec3(2, 2, 3));
            v.Add(new Geometry.Vec3(1, 3, 3));
            idx.AddRange(new[] { b, b + 1, b + 2 });
        }
        return new MeshData { Vertices = v.ToArray(), Indices = idx.ToArray() };
    }
}
