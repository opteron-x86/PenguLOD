namespace AnvilLOD.Meshes;

/// <summary>Full-model material before and after the record's alternate texture set.</summary>
public sealed record TextureMatchInput(string? Shape, string Diffuse, string? Normal, string DesiredDiffuse, string? DesiredNormal, bool Alternate);
