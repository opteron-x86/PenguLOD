using AnvilLOD.Core.Pipeline;

namespace AnvilLOD.Meshes.Authoring;

/// <summary>Tree reduction preserves alpha-tested foliage geometry, material boundaries and UV islands.</summary>
public static class TreeMeshReducer
{
    public sealed record Result(LodMesh Mesh,int OriginalTriangles,int Triangles,bool OverBudget);
    public static Result Reduce(LodMesh mesh,int budget,float maxError)
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
        int triangles=parts.Sum(p=>p.TriangleCount);
        return new(new LodMesh { Path=mesh.Path,Parts=parts,Warnings=mesh.Warnings },original,triangles,budget>0 && triangles>budget);
    }
}
