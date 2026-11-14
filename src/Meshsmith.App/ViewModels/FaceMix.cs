using System.IO;
using System.Text.Json;
using Meshsmith.Core.Report;

namespace Meshsmith.App.ViewModels;

/// <summary>Output faces grouped by surface kind, read back from the pipeline's faces.json.</summary>
public sealed record FaceMix(int Planes, int Cylinders, int Cones, int FacetedTriangles,
                             int Toruses = 0, int Spheres = 0)
{
    public static readonly FaceMix Empty = new(0, 0, 0, 0);

    // R3: the two new kinds count towards the mix. A faces.json that predates them deserializes to 0
    // (the int defaults on Summary), so an old file still reports exactly the mix it used to.
    public int Total => Planes + Cylinders + Cones + Toruses + Spheres;

    public double PlaneShare => Share(Planes);
    public double CylinderShare => Share(Cylinders);
    public double ConeShare => Share(Cones);

    private double Share(int count) => Total == 0 ? 0 : (double)count / Total;

    public static FaceMix FromFile(string facesJsonPath)
    {
        try
        {
            var document = JsonSerializer.Deserialize<FacesDocument>(File.ReadAllText(facesJsonPath));
            if (document is null) return Empty;
            return new FaceMix(document.Summary.Planes, document.Summary.Cylinders,
                               document.Summary.Cones, document.Summary.FacetedTriangles,
                               document.Summary.Toruses, document.Summary.Spheres);
        }
        catch (Exception)
        {
            // A missing or corrupt faces.json must never take the UI down; the numbers just stay empty.
            return Empty;
        }
    }
}
