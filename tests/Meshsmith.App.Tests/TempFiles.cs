using System.IO;

namespace Meshsmith.App.Tests;

internal static class TempFiles
{
    public static string Stl() => Write(".stl", "solid x\nendsolid x\n");

    public static string Json(string content) => Write(".json", content);

    public static string FacesJson(int planes = 2, int cylinders = 2, int cones = 0, int faceted = 0) =>
        Write(".json", $$"""
        {
          "schema": 1,
          "kind": "prismatic",
          "tolerance": 0.0171,
          "faces": [],
          "triangle_face": [],
          "summary": {
            "faces": {{planes + cylinders + cones}},
            "triangles": 2048,
            "planes": {{planes}},
            "cylinders": {{cylinders}},
            "cones": {{cones}},
            "faceted_triangles": {{faceted}},
            "edges": 12
          }
        }
        """);

    private static string Write(string extension, string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"meshsmith-ui-test-{Guid.NewGuid():N}{extension}");
        File.WriteAllText(path, content);
        return path;
    }
}
