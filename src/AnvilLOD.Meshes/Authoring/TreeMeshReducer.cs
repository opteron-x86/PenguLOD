using AnvilLOD.Core.Pipeline;

namespace AnvilLOD.Meshes.Authoring;

/// <summary>Tree reduction preserves alpha-tested foliage geometry, material boundaries and UV islands.</summary>
public static class TreeMeshReducer
{
    public sealed record Result(LodMesh Mesh,int OriginalTriangles,int Triangles,bool OverBudget,
        int FoliageTrianglesRemoved = 0, float FoliageCoverageLoss = 0, string? FoliageNote = null);
    public static Result Reduce(LodMesh mesh,int budget,float maxError, float? foliageLoss = null,
        Func<LodMaterial, Func<System.Numerics.Vector2, float>?>? opacity = null)
    {
        if(budget<0 || !float.IsFinite(maxError) || maxError<0)throw new ArgumentException("Invalid tree geometry budget or error bound.");
        int original=mesh.Parts.Sum(p=>p.TriangleCount);
        var parts=new List<LodMeshPart>();
        foreach(var part in mesh.Parts)
        {
            RunLimits.Check();
            // A textured leaf card cannot be shortened without cutting its alpha silhouette. Keep it whole.
            // Opaque trunks use half-edge QEM, which retains surviving UVs and prevents flipped faces.
            parts.Add(part.Material.HasAlpha || maxError==0 ? part : MeshSimplifier.Simplify(part,maxError));
        }
        var reduced = new LodMesh { Path=mesh.Path,Parts=parts,Warnings=mesh.Warnings };
        FoliageReducer.Result? foliage = null;
        if (budget > 0 && foliageLoss is { } loss && opacity is not null)
        { foliage = FoliageReducer.Reduce(reduced, budget, loss, opacity); reduced = foliage.Mesh; }
        int triangles=reduced.Parts.Sum(p=>p.TriangleCount);
        return new(reduced,original,triangles,budget>0 && triangles>budget,
            foliage?.RemovedTriangles ?? 0, foliage?.CoverageLoss ?? 0, foliage?.Note);
    }
}
