using System.Globalization;
using System.Text;
using Meshsmith.Core.Geometry;

namespace Meshsmith.Core.Mesh;

public sealed class StlFormatException(string message) : Exception(message);

public static class StlReader
{
    public static TriangleSoup Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return LooksBinary(bytes) ? ReadBinary(bytes) : ReadAscii(path);
    }

    private static bool LooksBinary(byte[] b)
    {
        if (b.Length < 84) return false;
        uint n = BitConverter.ToUInt32(b, 80);
        if (84L + 50L * n == b.Length) return true;
        var head = Encoding.ASCII.GetString(b, 0, Math.Min(512, b.Length)).TrimStart();
        return !(head.StartsWith("solid", StringComparison.OrdinalIgnoreCase)
                 && head.Contains("facet", StringComparison.OrdinalIgnoreCase));
    }

    private static TriangleSoup ReadBinary(byte[] b)
    {
        if (b.Length < 84) throw new StlFormatException("binary STL shorter than header");
        uint n = BitConverter.ToUInt32(b, 80);
        if (84L + 50L * n != b.Length)
            throw new StlFormatException($"declared {n} triangles, file length {b.Length} does not match");

        var pts = new Vec3[(int)n * 3];
        for (int t = 0; t < (int)n; t++)
        {
            int o = 84 + t * 50 + 12;   // skip the facet normal
            for (int k = 0; k < 3; k++)
            {
                float x = BitConverter.ToSingle(b, o + k * 12 + 0);
                float y = BitConverter.ToSingle(b, o + k * 12 + 4);
                float z = BitConverter.ToSingle(b, o + k * 12 + 8);
                pts[t * 3 + k] = new Vec3(x, y, z);
            }
        }
        return new TriangleSoup { Points = pts };
    }

    private static TriangleSoup ReadAscii(string path)
    {
        var text = File.ReadAllText(path);
        var nums = new List<double>(text.Length / 8);
        foreach (var tok in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            if (double.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                nums.Add(d);

        // each facet is: normal 3 + 3 vertices * 3 = 12 numbers
        if (nums.Count % 12 != 0 || nums.Count == 0)
            throw new StlFormatException($"ASCII STL: {nums.Count} numbers is not a multiple of 12");

        int n = nums.Count / 12;
        var pts = new Vec3[n * 3];
        for (int t = 0; t < n; t++)
            for (int k = 0; k < 3; k++)
            {
                int o = t * 12 + 3 + k * 3;
                pts[t * 3 + k] = new Vec3(nums[o], nums[o + 1], nums[o + 2]);
            }
        return new TriangleSoup { Points = pts };
    }
}
