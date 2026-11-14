using System.Text.Json;
using Meshsmith.Core.Pipeline;
using Meshsmith.Core.Report;
using Xunit;

namespace Meshsmith.Core.Tests;

/// <summary>
/// R4's two report fields follow the R3 rule: a new key is additive, and a document written before it
/// existed must still deserialise. These files are the App's input and are recorded in fixtures, so a
/// missing key that threw would break every already-written report on the user's disk.
/// </summary>
public class DecimationReportTests
{
    [Fact]
    public void An_older_faces_json_deserialises_with_the_decimation_fields_at_zero()
    {
        // Verbatim shape of a pre-R4 part-faces.json: schema 1, no decimate_* keys.
        const string older = """
        {
          "schema": 1,
          "kind": "prismatic",
          "tolerance": 0.0117,
          "faces": [],
          "triangle_face": [],
          "facet_regions": [],
          "summary": {
            "faces": 2,
            "triangles": 274,
            "planes": 2,
            "cylinders": 1,
            "cones": 1,
            "toruses": 0,
            "spheres": 0,
            "faceted_triangles": 0,
            "recon_coverage": 1.0,
            "region_fallbacks": 0,
            "edges": 12
          }
        }
        """;

        var doc = JsonSerializer.Deserialize<FacesDocument>(older);
        Assert.NotNull(doc);
        Assert.Equal(0, doc.Summary.DecimateFromTriangles);
        Assert.Equal(0.0, doc.Summary.DecimateDeviation);
        Assert.Equal(274, doc.Summary.Triangles);          // the rest still round-trips

        // And a decimated document carries the numbers through a full round trip.
        var decimated = new FacesDocument
        {
            Summary = new Summary
            {
                Triangles = 60_000,
                DecimateFromTriangles = 386_266,
                DecimateDeviation = 1.67
            }
        };
        var roundTripped = JsonSerializer.Deserialize<FacesDocument>(JsonSerializer.Serialize(decimated));
        Assert.NotNull(roundTripped);
        Assert.Equal(386_266, roundTripped.Summary.DecimateFromTriangles);
        Assert.Equal(1.67, roundTripped.Summary.DecimateDeviation, 6);

        // A conversion that did not decimate reports 0, so the App can tell the two apart.
        var result = ConversionPipeline.Run(StlFixtures.WriteCube(10), new ConversionOptions());
        Assert.Equal(0, result.DecimateFromTriangles);
        Assert.Equal(0.0, result.DecimateDeviation);
    }
}
