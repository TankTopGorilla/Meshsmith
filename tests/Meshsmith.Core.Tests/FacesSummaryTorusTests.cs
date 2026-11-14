using System.Text.Json;
using Meshsmith.Core.Report;
using Xunit;

namespace Meshsmith.Core.Tests;

/// <summary>
/// The report is a documented schema and the App reads it (FaceMix). Two new counts must appear and,
/// just as important, an OLD faces.json without them must still deserialize - every existing
/// record/replay fixture and the App's TempFiles.FacesJson are in that shape.
/// </summary>
public class FacesSummaryTorusTests
{
    [Fact]
    public void A_summary_carries_torus_and_sphere_counts()
    {
        var json = JsonSerializer.Serialize(new FacesDocument
        {
            Summary = new Summary { Faces = 3, Toruses = 1, Spheres = 2 }
        });
        Assert.Contains("\"toruses\":1", json.Replace(" ", ""));
        Assert.Contains("\"spheres\":2", json.Replace(" ", ""));
    }

    [Fact]
    public void An_old_faces_json_without_the_new_keys_still_deserializes_to_zero()
    {
        const string old = """
        {
          "schema": 1,
          "kind": "prismatic",
          "tolerance": 0.0171,
          "faces": [],
          "triangle_face": [],
          "summary": {
            "faces": 5, "triangles": 2048, "planes": 2, "cylinders": 2,
            "cones": 1, "faceted_triangles": 0, "edges": 12
          }
        }
        """;
        var doc = JsonSerializer.Deserialize<FacesDocument>(old);
        Assert.NotNull(doc);
        Assert.Equal(0, doc!.Summary.Toruses);
        Assert.Equal(0, doc.Summary.Spheres);
        Assert.Equal(2, doc.Summary.Planes);
    }
}
