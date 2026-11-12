using Meshsmith.Core.Fit;
using Meshsmith.Core.Geometry;
using Meshsmith.Core.Mesh;
using Occt;

namespace Meshsmith.Core.Validation;

public static class DeviationChecker
{
    /// <summary>Farthest sampled distance mesh->model and model->mesh, in model units.</summary>
    public static double Compute(MeshData mesh, IReadOnlyList<ClassifiedRegion> regions,
                                 int[] triangleRegion, TopoDS_Shape shape, double tol)
    {
        double worst = 0;

        // mesh -> model: every triangle vertex and centroid against its own face's surface
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            int id = triangleRegion[t];
            if (id < 0 || id >= regions.Count) continue;
            var prim = regions[id].Primitive;
            if (prim is FacetedPrimitive) continue;

            foreach (var p in new[] { mesh.V0(t), mesh.V1(t), mesh.V2(t), mesh.Centroid(t) })
                worst = Math.Max(worst, Math.Abs(prim.DistanceTo(p)));
        }

        // model -> mesh: tessellate the ACTUAL faces (points inside the trims) and measure
        // every node against the mesh. Projecting a bounding box onto the primitive samples
        // the far side of a cylinder and reports a huge fake deviation.
        _ = new BRepMesh_IncrementalMesh(shape, Math.Max(tol * 0.25, 1e-4), false, 0.3);
        var grid = new TriangleGrid(mesh);

        var ex = new TopExp_Explorer(shape, TopAbs_ShapeEnum.TopAbs_FACE);
        while (ex.More)
        {
            var face = (TopoDS_Face)ex.Current;
            var tri = BRep_Tool.Triangulation(face, out _);
            if (tri != null)
                for (int i = 1; i <= tri.NbNodes; i++)
                    worst = Math.Max(worst, grid.DistanceToSurface(Vec3.From(tri.Node(i))));
            ex.Next();   // unconditional: a continue that skips this is an infinite native loop
        }

        return worst;
    }
}
