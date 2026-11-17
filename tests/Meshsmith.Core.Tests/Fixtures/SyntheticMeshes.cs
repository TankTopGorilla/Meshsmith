using Meshsmith.Core.Geometry;
using Meshsmith.Core.Mesh;

namespace Meshsmith.Core.Tests.Fixtures;

/// <summary>
/// The synthetic bodies the hypothesis segmenter is measured on. Built in-test with no fixtures on
/// disk: the point of these tests is that the answer is known exactly, so the mesh must be generated
/// from the same numbers the assertion quotes.
///
/// Every builder returns a welded-by-INDEX mesh (indices are shared by construction) so
/// <see cref="MeshTopology"/> sees real adjacency. Welding an unindexed soup instead would work too,
/// but a coincident-vertex weld can merge two bodies that merely touch, which would make a
/// two-primitive scene a one-body scene without saying so.
/// </summary>
internal static class SyntheticMeshes
{
    /// <summary>A flat grid in the z=0 plane: (nx × ny) quads, 2 triangles each.</summary>
    public static MeshData Plane(double size, int nx, int ny)
    {
        var v = new List<Vec3>();
        var idx = new List<int>();
        for (int j = 0; j <= ny; j++)
            for (int i = 0; i <= nx; i++)
                v.Add(new Vec3(size * i / nx, size * j / ny, 0));

        int Id(int i, int j) => j * (nx + 1) + i;
        for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                idx.AddRange(new[] { Id(i, j), Id(i + 1, j), Id(i + 1, j + 1) });
                idx.AddRange(new[] { Id(i, j), Id(i + 1, j + 1), Id(i, j + 1) });
            }
        return new MeshData { Vertices = v.ToArray(), Indices = idx.ToArray() };
    }

    /// <summary>
    /// An open cylinder SIDE only - no caps, no axis vertex. An open side is one connected patch that
    /// is a cylinder and nothing else, which is what makes the cylinder assertion unambiguous; with
    /// caps the body also contains two planes and "recovered as a cylinder" would be a share, not a fact.
    /// </summary>
    public static MeshData CylinderSide(double radius, double height, int n)
    {
        var v = new List<Vec3>();
        var idx = new List<int>();
        for (int i = 0; i < n; i++)
        {
            double a = 2 * Math.PI * i / n;
            v.Add(new Vec3(radius * Math.Cos(a), radius * Math.Sin(a), 0));
            v.Add(new Vec3(radius * Math.Cos(a), radius * Math.Sin(a), height));
        }
        for (int i = 0; i < n; i++)
        {
            int a = 2 * i, b = 2 * ((i + 1) % n);
            idx.AddRange(new[] { a, b, b + 1 });
            idx.AddRange(new[] { a, b + 1, a + 1 });
        }
        return new MeshData { Vertices = v.ToArray(), Indices = idx.ToArray() };
    }

    /// <summary>
    /// A truncated cone side, radii r1 (base, z=0) to r2 (top, z=height). Its half-angle at the APEX
    /// is atan(|r1 - r2| / height) - for 14 -> 4 over 30 that is atan(1/3) = 18.4349 degrees, which is
    /// what <c>Fitters.FitCone</c> returns. The apex lies above the top face when r2 &lt; r1.
    /// </summary>
    public static MeshData Cone(double r1, double r2, double height, int n)
    {
        var v = new List<Vec3>();
        var idx = new List<int>();
        for (int i = 0; i < n; i++)
        {
            double a = 2 * Math.PI * i / n;
            v.Add(new Vec3(r1 * Math.Cos(a), r1 * Math.Sin(a), 0));
            v.Add(new Vec3(r2 * Math.Cos(a), r2 * Math.Sin(a), height));
        }
        for (int i = 0; i < n; i++)
        {
            int a = 2 * i, b = 2 * ((i + 1) % n);
            idx.AddRange(new[] { a, b, b + 1 });
            idx.AddRange(new[] { a, b + 1, a + 1 });
        }
        return new MeshData { Vertices = v.ToArray(), Indices = idx.ToArray() };
    }

    /// <summary>A UV sphere with (stacks-1) latitude rings of `slices` vertices plus two poles:
    /// 2 * slices * (stacks - 1) triangles.</summary>
    public static MeshData Sphere(double radius, int stacks, int slices,
                                  double cx = 0, double cy = 0, double cz = 0)
    {
        var v = new List<Vec3>();
        var idx = new List<int>();
        v.Add(new Vec3(cx, cy, cz + radius));
        for (int i = 1; i < stacks; i++)
        {
            double phi = Math.PI * i / stacks;
            double z = radius * Math.Cos(phi), rr = radius * Math.Sin(phi);
            for (int j = 0; j < slices; j++)
            {
                double th = 2 * Math.PI * j / slices;
                v.Add(new Vec3(cx + rr * Math.Cos(th), cy + rr * Math.Sin(th), cz + z));
            }
        }
        int south = v.Count;
        v.Add(new Vec3(cx, cy, cz - radius));

        int Ring(int i, int j) => 1 + (i - 1) * slices + (j % slices);
        for (int j = 0; j < slices; j++) idx.AddRange(new[] { 0, Ring(1, j), Ring(1, j + 1) });
        for (int i = 1; i < stacks - 1; i++)
            for (int j = 0; j < slices; j++)
            {
                idx.AddRange(new[] { Ring(i, j), Ring(i + 1, j), Ring(i + 1, j + 1) });
                idx.AddRange(new[] { Ring(i, j), Ring(i + 1, j + 1), Ring(i, j + 1) });
            }
        for (int j = 0; j < slices; j++)
            idx.AddRange(new[] { south, Ring(stacks - 1, j + 1), Ring(stacks - 1, j) });
        return new MeshData { Vertices = v.ToArray(), Indices = idx.ToArray() };
    }

    /// <summary>A torus about the Z axis: N steps around the major circle, M around the tube.</summary>
    public static MeshData Torus(double major, double minor, int N, int M,
                                 double dx = 0, double dy = 0, double dz = 0)
    {
        var v = new List<Vec3>();
        var idx = new List<int>();
        for (int i = 0; i < N; i++)
        {
            double u = 2 * Math.PI * i / N;
            for (int j = 0; j < M; j++)
            {
                double w = 2 * Math.PI * j / M;
                v.Add(new Vec3(dx + (major + minor * Math.Cos(w)) * Math.Cos(u),
                               dy + (major + minor * Math.Cos(w)) * Math.Sin(u),
                               dz + minor * Math.Sin(w)));
            }
        }
        int Id(int i, int j) => (i % N) * M + (j % M);
        for (int i = 0; i < N; i++)
            for (int j = 0; j < M; j++)
            {
                idx.AddRange(new[] { Id(i, j), Id(i + 1, j), Id(i + 1, j + 1) });
                idx.AddRange(new[] { Id(i, j), Id(i + 1, j + 1), Id(i, j + 1) });
            }
        return new MeshData { Vertices = v.ToArray(), Indices = idx.ToArray() };
    }

    /// <summary>
    /// A knurled band: `cells` flat square patches laid around a cylinder of radius R, each patch
    /// `trisPerPatch` triangles (a cell is trisPerPatch/2 quads, so 8 = 2x2 quads, 24 = 3x4). The
    /// patches are DISCONNECTED BY CONSTRUCTION - they do not share vertices - so flood fill splits
    /// them at any threshold and the only open question is whether the classifier's floor accepts them.
    /// This is the fixture R2's "tiny planes" claim is measured on.
    ///
    /// A patch of 8 triangles is below MinPieceTriangles (16) and must stay faceted; 24 is above it and
    /// must come back as one plane per patch. Both directions are asserted (T9 / T10).
    /// </summary>
    public static MeshData KnurlBand(int cells, int trisPerPatch,
                                     double radius = 12, double height = 20, double bandWidth = 4)
    {
        if (trisPerPatch % 2 != 0) throw new ArgumentException("a patch is whole quads", nameof(trisPerPatch));
        int quadsPerPatch = trisPerPatch / 2;

        // Lay the quads out as a near-square block: qx columns around the band, qy rows up it.
        int qx = (int)Math.Round(Math.Sqrt(quadsPerPatch));
        while (quadsPerPatch % qx != 0) qx--;
        int qy = quadsPerPatch / qx;

        var v = new List<Vec3>();
        var idx = new List<int>();
        double circumference = 2 * Math.PI * radius;

        for (int c = 0; c < cells; c++)
        {
            // A gap between patches keeps them topologically separate. The gap is a fraction of the
            // patch so the band stays a band at every patch size.
            double u0 = circumference * c / cells;
            double du = circumference / cells * 0.62;
            double z0 = -bandWidth / 2, dz = bandWidth / qy;
            int baseIndex = v.Count;
            int dw = qx;

            for (int j = 0; j <= qy; j++)
                for (int i = 0; i <= dw; i++)
                {
                    double u = u0 + du * i / dw;
                    double a = u / radius;
                    double z = z0 + dz * j;
                    v.Add(new Vec3(radius * Math.Cos(a), radius * Math.Sin(a), height / 2 + z));
                }

            int Id(int i, int j) => baseIndex + j * (dw + 1) + i;
            for (int j = 0; j < qy; j++)
                for (int i = 0; i < dw; i++)
                {
                    idx.AddRange(new[] { Id(i, j), Id(i + 1, j), Id(i + 1, j + 1) });
                    idx.AddRange(new[] { Id(i, j), Id(i + 1, j + 1), Id(i, j + 1) });
                }
        }
        return new MeshData { Vertices = v.ToArray(), Indices = idx.ToArray() };
    }

    /// <summary>
    /// A CONNECTED shallow-ribbed body: a cylinder side carrying axial ribs whose crest dihedral is
    /// kept under the grower's 12 deg threshold, plus a dome on top. This is R2's shape, and its whole
    /// point is that flood fill CANNOT split it: every dihedral is under the threshold, so the grower
    /// returns one region and no single primitive fits it. A disconnected band would be trivially
    /// segmented by flood and would prove nothing.
    ///
    /// `ribs` crests of `amplitude`, all shallow angles; `stacks` rows up the side.
    /// </summary>
    public static MeshData R2Drum(int ribs = 24, int segmentsPerRib = 16, int stacks = 40,
                                  double radius = 12, double height = 32,
                                  double amplitude = 0.55, double domeHeight = 12,
                                  int domeRings = 8, int domeSlices = 96)
    {
        var v = new List<Vec3>();
        var idx = new List<int>();

        int around = ribs * segmentsPerRib;

        // Side: r(u) = R + amplitude * (1 - cos(ribs*u))/2 - a smooth crest, so the dihedral between
        // adjacent facets stays well under 12 deg for the segment count above.
        for (int j = 0; j <= stacks; j++)
        {
            double z = height * j / stacks;
            for (int i = 0; i < around; i++)
            {
                double a = 2 * Math.PI * i / around;
                double r = radius + amplitude * (1 - Math.Cos(ribs * a)) / 2;
                v.Add(new Vec3(r * Math.Cos(a), r * Math.Sin(a), z));
            }
        }
        int Side(int i, int j) => j * around + (i % around);
        for (int j = 0; j < stacks; j++)
            for (int i = 0; i < around; i++)
            {
                idx.AddRange(new[] { Side(i, j), Side(i + 1, j), Side(i + 1, j + 1) });
                idx.AddRange(new[] { Side(i, j), Side(i + 1, j + 1), Side(i, j + 1) });
            }

        // Dome: a half-ellipsoid on top of the side's top ring, sharing its boundary vertices so the
        // body is ONE connected patch with no seam for the grower to stop at.
        int domeBase = v.Count;
        for (int k = 1; k <= domeRings; k++)
        {
            double phi = Math.PI / 2 * k / domeRings;
            double rr = radius * Math.Cos(phi);
            double z = height + domeHeight * Math.Sin(phi);
            for (int i = 0; i < around; i++)
            {
                double a = 2 * Math.PI * i / around;
                v.Add(new Vec3(rr * Math.Cos(a), rr * Math.Sin(a), z));
            }
        }
        int pole = v.Count;
        v.Add(new Vec3(0, 0, height + domeHeight));

        int Dome(int i, int k) => domeBase + (k - 1) * around + (i % around);
        for (int i = 0; i < around; i++)
        {
            // the rim uses the SIDE's last ring so the two surfaces share vertices
            idx.AddRange(new[] { Side(i, stacks), Side(i + 1, stacks), Dome(i + 1, 1) });
            idx.AddRange(new[] { Side(i, stacks), Dome(i + 1, 1), Dome(i, 1) });
        }
        for (int k = 1; k < domeRings; k++)
            for (int i = 0; i < around; i++)
            {
                idx.AddRange(new[] { Dome(i, k), Dome(i + 1, k), Dome(i + 1, k + 1) });
                idx.AddRange(new[] { Dome(i, k), Dome(i + 1, k + 1), Dome(i, k + 1) });
            }
        for (int i = 0; i < around; i++)
            idx.AddRange(new[] { Dome(i, domeRings), Dome(i + 1, domeRings), pole });

        return new MeshData { Vertices = v.ToArray(), Indices = idx.ToArray() };
    }

    /// <summary>
    /// A plane and a cylinder side standing on it, far enough apart that they share no vertex. The
    /// split's expected triangle counts are exact by construction: 24,000 for the cylinder side and
    /// 2*86*86 = 14,792 for the plane.
    /// </summary>
    public static MeshData TwoPrimitive()
    {
        var plane = Plane(60, 86, 86);           // (86)^2 quads -> 14,792 triangles
        var cylinder = CylinderSide(12, 40, 300); // 300 segments -> 600 quads -> 24,000 triangles

        var v = new List<Vec3>(plane.Vertices.Length + cylinder.Vertices.Length);
        v.AddRange(plane.Vertices);
        v.AddRange(cylinder.Vertices.Select(p => p + new Vec3(80, 80, 0)));

        var idx = new List<int>(plane.Indices.Length + cylinder.Indices.Length);
        idx.AddRange(plane.Indices);
        idx.AddRange(cylinder.Indices.Select(i => i + plane.Vertices.Length));

        return new MeshData { Vertices = v.ToArray(), Indices = idx.ToArray() };
    }

    // ---- helpers ----------------------------------------------------------------------

    /// <summary>
    /// A deterministic Gaussian perturbation of every vertex, in units of `tol`. Deterministic on
    /// purpose: a test that measures "doesit recover" must not change its answer between runs.
    /// </summary>
    public static MeshData Jitter(MeshData mesh, double sigma, double tol, ulong seed = 0x9E3779B97F4A7C15UL)
    {
        ulong s = seed;
        double NextGaussian()
        {
            // Box-Muller on a xorshift stream: two uniforms in, one normal out.
            double U1() { s ^= s >> 12; s ^= s << 25; s ^= s >> 27; return ((s * 2685821657736338717UL) >> 11) * (1.0 / (1UL << 53)); }
            double u1 = Math.Max(U1(), 1e-12), u2 = U1();
            return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }

        double scale = sigma * tol;
        var moved = mesh.Vertices.Select(p =>
            p + new Vec3(NextGaussian() * scale, NextGaussian() * scale, NextGaussian() * scale)).ToArray();
        return new MeshData { Vertices = moved, Indices = mesh.Indices };
    }

    /// <summary>The pipeline's own tolerance rule, so a test measures what a part would.</summary>
    public static double Tol(MeshData mesh) => Math.Clamp(2e-4 * mesh.Diagonal, 1e-3, 0.5);
}
