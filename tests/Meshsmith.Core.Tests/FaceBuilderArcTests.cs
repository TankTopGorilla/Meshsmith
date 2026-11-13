using Meshsmith.Core.Brep;
using Meshsmith.Core.Fit;
using Meshsmith.Core.Geometry;
using Occt;
using Xunit;

namespace Meshsmith.Core.Tests;

public class FaceBuilderArcTests
{
    /// <summary>
    /// A fitted boundary arc never passes exactly through the loop's corner points - it passes within
    /// the fit tolerance, which is 1e-6..1e-2 on a real mesh. BRepBuilderAPI_MakeEdge projects each
    /// vertex onto the pcurve at Precision::Confusion (1e-7) and throws
    /// BRepBuilderAPI_PointProjectionFailed when the residual is bigger, which is what facets 7 of
    /// clamp.stl's regions. This loop is a cylinder band with rounded ends; its ends are sampled from
    /// an ellipse 2e-4 off round, so the fitted circles miss the corners by ~2e-4.
    /// </summary>
    [Fact]
    public void Cylinder_band_with_fitted_arcs_builds_a_face()
    {
        double radius = 10, height = 6;
        var prim = new CylinderPrimitive
        {
            PointOnAxis = new Vec3(0, 0, 0), Axis = new Vec3(0, 0, 1), Radius = radius
        };
        var surface = SurfaceFactory.Create(prim);
        var loop = Capsule(u0: 1.0, width: 1.5, height: height, a: 0.5, b: 0.5 - 2e-4, steps: 32);

        var faces = FaceBuilder.Build(surface, prim, new[] { loop }, uvTol: 1e-3,
                                      samplePoint: Vec3.From(surface.Value(2.0, 3.0)),
                                      sampleNormal: new Vec3(1, 0, 0));

        var face = Assert.Single(faces);
        Assert.True(face.Area > 0);
    }

    /// <summary>Closed uv loop: two straight sides at v=0 and v=height, two half-ellipse ends about
    /// (u0, height/2) and (u0+width, height/2).</summary>
    private static (double U, double V)[] Capsule(double u0, double width, double height,
                                                  double a, double b, int steps)
    {
        var pts = new List<(double U, double V)>();
        double cv = height / 2;

        for (int i = 0; i <= steps; i++) pts.Add((u0 + width * i / (double)steps, 0));

        for (int i = 1; i <= steps; i++)
        {
            double t = -Math.PI / 2 + Math.PI * i / steps;
            pts.Add((u0 + width + a * Math.Cos(t), cv + b * Math.Sin(t)));
        }

        for (int i = 1; i <= steps; i++) pts.Add((u0 + width - width * i / (double)steps, height));

        for (int i = 1; i <= steps; i++)
        {
            double t = Math.PI / 2 + Math.PI * i / steps;
            pts.Add((u0 - a * Math.Cos(t - Math.PI), cv + b * Math.Sin(t)));
        }

        pts.Add(pts[0]);
        return pts.ToArray();
    }
}
