using Meshsmith.Core.Geometry;
using Meshsmith.Core.Mesh;

namespace Meshsmith.Core.Seg;

/// <summary>
/// A uniform grid over TRIANGLE CENTROIDS - the membership index the hypothesis segmenter needs.
///
/// <see cref="Mesh.TriangleGrid"/> is deliberately not reused: it answers point-to-surface distance
/// over BOUNDING-BOX cells (10-30x over-fetch on a knurled band) and exposes neither cell-to-centroid
/// membership nor cell iteration, both of which the RANSAC scoring loop is built on. This type does
/// one job: given a cell id, hand back the triangles whose centroid falls in it.
///
/// Everything is built in triangle order and every accessor returns ids in ascending order, because
/// the segmenter's determinism rule is "iteration is always over ascending arrays" - a hash
/// enumeration order that leaked out here would make the region partition machine-dependent.
/// </summary>
public sealed class CentroidGrid
{
    /// <summary>Half the diagonal of a unit cube: the farthest a point inside a cell can be from
    /// that cell's centre. This is the cull slack that makes the cell test sound.</summary>
    public const double CellRadiusFactor = 0.8660254037844386;

    private readonly Vec3 _min;
    private readonly double _cell;
    private readonly int _nx, _ny, _nz;
    private readonly List<int>?[] _cells;
    private readonly Vec3[] _cellCentre;

    public CentroidGrid(MeshData mesh, int divisions = 64)
    {
        if (divisions < 1) throw new ArgumentOutOfRangeException(nameof(divisions));

        var (lo, hi) = CentroidBounds(mesh);
        _min = lo;

        var size = hi - lo;
        double longest = Math.Max(size.X, Math.Max(size.Y, size.Z));
        _cell = Math.Max(longest / divisions, 1e-9);

        // +1 because a centroid exactly on the far face must still land inside a cell, and the
        // clamp in CellOf would otherwise pile the whole far slab into the last cell.
        _nx = Math.Max(1, (int)Math.Ceiling(size.X / _cell) + 1);
        _ny = Math.Max(1, (int)Math.Ceiling(size.Y / _cell) + 1);
        _nz = Math.Max(1, (int)Math.Ceiling(size.Z / _cell) + 1);

        _cells = new List<int>?[_nx * _ny * _nz];
        _cellCentre = new Vec3[_cells.Length];
        for (int z = 0; z < _nz; z++)
            for (int y = 0; y < _ny; y++)
                for (int x = 0; x < _nx; x++)
                    _cellCentre[Index(x, y, z)] =
                        _min + new Vec3((x + 0.5) * _cell, (y + 0.5) * _cell, (z + 0.5) * _cell);

        var occupied = new List<int>();
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            int id = CellOf(mesh.Centroid(t));
            if (_cells[id] is null)
            {
                _cells[id] = new List<int>();
                occupied.Add(id);
            }
            _cells[id]!.Add(t);
        }
        occupied.Sort();
        OccupiedCells = occupied;
    }

    /// <summary>The cell size along the longest axis. The cull's slack is derived from it.</summary>
    public double CellSize => _cell;

    /// <summary>The farthest a centroid in a cell can lie from that cell's centre.</summary>
    public double CellRadius => _cell * CellRadiusFactor;

    /// <summary>Ids of the cells that hold at least one triangle centroid, ascending. The scoring
    /// loop iterates this, so the order is part of the contract, not an accident.</summary>
    public IReadOnlyList<int> OccupiedCells { get; }

    public int CellCount => _cells.Length;

    /// <summary>The cell a point falls in. Clamped, so a centroid on the upper bound cannot index
    /// past the array.</summary>
    public int CellOf(Vec3 p)
    {
        var d = p - _min;
        int x = Math.Clamp((int)(d.X / _cell), 0, _nx - 1);
        int y = Math.Clamp((int)(d.Y / _cell), 0, _ny - 1);
        int z = Math.Clamp((int)(d.Z / _cell), 0, _nz - 1);
        return Index(x, y, z);
    }

    public Vec3 CellCentre(int cellId) => _cellCentre[cellId];

    /// <summary>The triangles in one cell, ascending; null when the cell is empty.</summary>
    public IReadOnlyList<int>? Cell(int cellId) => _cells[cellId];

    /// <summary>
    /// The (2*ring+1)^3 block of cells around <paramref name="cellId"/>, appended to
    /// <paramref name="into"/> in ascending triangle order. Empty cells contribute nothing. The block
    /// is clipped at the grid bounds rather than wrapped: a seed near the edge gets a smaller block,
    /// which is why the caller must still check the count it got.
    /// </summary>
    public void Collect(int cellId, int ring, List<int> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        if (ring < 0) throw new ArgumentOutOfRangeException(nameof(ring));

        int cx = cellId % _nx;
        int cy = (cellId / _nx) % _ny;
        int cz = cellId / (_nx * _ny);

        for (int z = Math.Max(0, cz - ring); z <= Math.Min(_nz - 1, cz + ring); z++)
            for (int y = Math.Max(0, cy - ring); y <= Math.Min(_ny - 1, cy + ring); y++)
                for (int x = Math.Max(0, cx - ring); x <= Math.Min(_nx - 1, cx + ring); x++)
                    if (_cells[Index(x, y, z)] is { } cell)
                        into.AddRange(cell);

        into.Sort();
    }

    private int Index(int x, int y, int z) => (z * _ny + y) * _nx + x;

    private static (Vec3 Min, Vec3 Max) CentroidBounds(MeshData mesh)
    {
        if (mesh.TriangleCount == 0) return (Vec3.Zero, Vec3.Zero);

        var min = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue);
        var max = new Vec3(double.MinValue, double.MinValue, double.MinValue);
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            var c = mesh.Centroid(t);
            min = new Vec3(Math.Min(min.X, c.X), Math.Min(min.Y, c.Y), Math.Min(min.Z, c.Z));
            max = new Vec3(Math.Max(max.X, c.X), Math.Max(max.Y, c.Y), Math.Max(max.Z, c.Z));
        }
        return (min, max);
    }
}
