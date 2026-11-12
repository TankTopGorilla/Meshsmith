using Meshsmith.Core.Geometry;
using Meshsmith.Core.Mesh;

internal static class StlFixtures
{
    private static string TempStl(string name) =>
        Path.Combine(Path.GetTempPath(), $"cw-{name}-{Guid.NewGuid():N}.stl");

    public static string WriteCube(double size) => WriteTriangles("cube", Cube(size));
    public static string WriteCylinder(double radius, double height, int segments) =>
        WriteTriangles("cyl", Cylinder(radius, height, segments));
    public static string WriteWasher(double outer, double inner, double height, int segments) =>
        WriteTriangles("washer", Washer(outer, inner, height, segments));
    public static string WriteCone(double r1, double r2, double height, int segments) =>
        WriteTriangles("cone", Cone(r1, r2, height, segments));

    public static List<(Vec3 A, Vec3 B, Vec3 C)> Cube(double s)
    {
        var v = new[]
        {
            new Vec3(0,0,0), new Vec3(s,0,0), new Vec3(s,s,0), new Vec3(0,s,0),
            new Vec3(0,0,s), new Vec3(s,0,s), new Vec3(s,s,s), new Vec3(0,s,s),
        };
        // outward winding (right-hand rule) so SignedVolume is positive
        int[] quads = { 0,3,2,1,  4,5,6,7,  0,1,5,4,  1,2,6,5,  2,3,7,6,  3,0,4,7 };
        var tris = new List<(Vec3, Vec3, Vec3)>();
        for (int q = 0; q < quads.Length; q += 4)
        {
            var a = v[quads[q]]; var b = v[quads[q + 1]];
            var c = v[quads[q + 2]]; var d = v[quads[q + 3]];
            tris.Add((a, b, c)); tris.Add((a, c, d));
        }
        return tris;
    }

    public static List<(Vec3 A, Vec3 B, Vec3 C)> Cylinder(double r, double h, int n)
    {
        var tris = new List<(Vec3, Vec3, Vec3)>();
        for (int i = 0; i < n; i++)
        {
            double a0 = 2 * Math.PI * i / n, a1 = 2 * Math.PI * (i + 1) / n;
            var p0 = new Vec3(r * Math.Cos(a0), r * Math.Sin(a0), 0);
            var p1 = new Vec3(r * Math.Cos(a1), r * Math.Sin(a1), 0);
            var q0 = new Vec3(p0.X, p0.Y, h);
            var q1 = new Vec3(p1.X, p1.Y, h);
            tris.Add((p0, p1, q1)); tris.Add((p0, q1, q0));      // side
            tris.Add((new Vec3(0, 0, 0), p1, p0));               // bottom
            tris.Add((new Vec3(0, 0, h), q0, q1));               // top
        }
        return tris;
    }

    public static List<(Vec3 A, Vec3 B, Vec3 C)> Washer(double ro, double ri, double h, int n)
    {
        var tris = new List<(Vec3, Vec3, Vec3)>();
        for (int i = 0; i < n; i++)
        {
            double a0 = 2 * Math.PI * i / n, a1 = 2 * Math.PI * (i + 1) / n;
            var o0 = new Vec3(ro * Math.Cos(a0), ro * Math.Sin(a0), 0);
            var o1 = new Vec3(ro * Math.Cos(a1), ro * Math.Sin(a1), 0);
            var i0 = new Vec3(ri * Math.Cos(a0), ri * Math.Sin(a0), 0);
            var i1 = new Vec3(ri * Math.Cos(a1), ri * Math.Sin(a1), 0);
            var O0 = new Vec3(o0.X, o0.Y, h); var O1 = new Vec3(o1.X, o1.Y, h);
            var I0 = new Vec3(i0.X, i0.Y, h); var I1 = new Vec3(i1.X, i1.Y, h);

            tris.Add((o0, o1, O1)); tris.Add((o0, O1, O0));      // outer wall
            tris.Add((i1, i0, I0)); tris.Add((i1, I0, I1));      // inner wall
            tris.Add((i0, i1, o1)); tris.Add((i0, o1, o0));      // bottom annulus
            tris.Add((I1, I0, O0)); tris.Add((I1, O0, O1));      // top annulus
        }
        return tris;
    }

    public static List<(Vec3 A, Vec3 B, Vec3 C)> Cone(double r1, double r2, double h, int n)
    {
        var tris = new List<(Vec3, Vec3, Vec3)>();
        for (int i = 0; i < n; i++)
        {
            double a0 = 2 * Math.PI * i / n, a1 = 2 * Math.PI * (i + 1) / n;
            var b0 = new Vec3(r1 * Math.Cos(a0), r1 * Math.Sin(a0), 0);
            var b1 = new Vec3(r1 * Math.Cos(a1), r1 * Math.Sin(a1), 0);
            var t0 = new Vec3(r2 * Math.Cos(a0), r2 * Math.Sin(a0), h);
            var t1 = new Vec3(r2 * Math.Cos(a1), r2 * Math.Sin(a1), h);
            tris.Add((b0, b1, t1)); tris.Add((b0, t1, t0));
            tris.Add((new Vec3(0, 0, 0), b1, b0));
            tris.Add((new Vec3(0, 0, h), t0, t1));
        }
        return tris;
    }

    private static string WriteTriangles(string name, List<(Vec3 A, Vec3 B, Vec3 C)> tris)
    {
        var path = TempStl(name);
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        w.Write(new byte[80]);
        w.Write((uint)tris.Count);
        foreach (var (a, b, c) in tris)
        {
            var n = Vec3.Cross(b - a, c - a).Normalized();
            w.Write((float)n.X); w.Write((float)n.Y); w.Write((float)n.Z);
            foreach (var p in new[] { a, b, c })
            {
                w.Write((float)p.X); w.Write((float)p.Y); w.Write((float)p.Z);
            }
            w.Write((ushort)0);
        }
        return path;
    }

    public static void WriteRepositoryFixtures(string repoFixturesDir)
    {
        Directory.CreateDirectory(repoFixturesDir);
        File.Copy(WriteCube(10), Path.Combine(repoFixturesDir, "cube_10.stl"), true);
        File.Copy(WriteCylinder(20, 30, 256), Path.Combine(repoFixturesDir, "cylinder_r20_h30.stl"), true);
        File.Copy(WriteWasher(30, 10, 10, 256), Path.Combine(repoFixturesDir, "washer_r30_r10_h10.stl"), true);
        File.Copy(WriteCone(30, 10, 20, 256), Path.Combine(repoFixturesDir, "cone_r30_r10_h20.stl"), true);
    }
}
