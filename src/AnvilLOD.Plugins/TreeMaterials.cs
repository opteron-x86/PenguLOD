using AnvilLOD.Core.Lod;
using AnvilLOD.Core.World;
using AnvilLOD.Meshes;
using Mutagen.Bethesda.Skyrim;

namespace AnvilLOD.Plugins;

internal static class TreeMaterials
{
    public static TreeMaterialVariant? Read(GameContext game, IModelGetter? model)
    {
        if (model?.AlternateTextures is not { Count: > 0 }) return null;
        var overrides = new List<TreeTextureOverride>();
        string? error = null;
        foreach (var alt in model.AlternateTextures)
        {
            if (!game.LinkCache.TryResolve<ITextureSetGetter>(alt.NewTexture.FormKey, out var set))
            {
                error = $"Unresolved alternate texture set {alt.NewTexture.FormKey} for shape '{alt.Name}'.";
                continue;
            }
            // TXST record order differs from the BSShaderTextureSet slot order.
            string?[] slots = [set.Diffuse?.DataRelativePath.Path, set.NormalOrGloss?.DataRelativePath.Path,
                set.GlowOrDetailMap?.DataRelativePath.Path, set.Height?.DataRelativePath.Path,
                set.Environment?.DataRelativePath.Path, set.EnvironmentMaskOrSubsurfaceTint?.DataRelativePath.Path,
                set.Multilayer?.DataRelativePath.Path, set.BacklightMaskOrSpecular?.DataRelativePath.Path];
            overrides.Add(new(alt.Name, (int)alt.Index, slots));
        }
        return TreeMaterialVariant.Create(model.File.DataRelativePath.Path, overrides, error);
    }

    public static bool TryApply(LodMesh source, TreeMaterialVariant? variant, out LodMesh result, out string? reason)
    {
        result = source;
        reason = variant?.Error;
        if (reason is not null) return false;
        if (variant is null) return true;
        var parts = source.Parts.ToArray();
        var changed = new HashSet<int>();
        foreach (var entry in variant.Overrides)
        {
            var matches = Enumerable.Range(0, parts.Length).Where(i =>
                !string.IsNullOrWhiteSpace(entry.ShapeName) && string.Equals(parts[i].Name, entry.ShapeName, StringComparison.OrdinalIgnoreCase)).ToArray();
            int index = matches.Length == 1 ? matches[0]
                : matches.Contains(entry.ShapeIndex) ? entry.ShapeIndex : -1;
            if (index < 0 || !changed.Add(index))
            {
                reason = $"Alternate texture shape '{entry.ShapeName}' (index {entry.ShapeIndex}) is missing or ambiguous.";
                return false;
            }
            var part = parts[index];
            var slots = part.Material.Textures.Concat(Enumerable.Repeat("", Math.Max(0, 9 - part.Material.Textures.Count))).ToArray();
            for (int slot = 0; slot < Math.Min(slots.Length, entry.Slots.Count); slot++)
                if (entry.Slots[slot] is { Length: > 0 } path) slots[slot] = GamePath.NormalizeTexture(path);
            parts[index] = WithMaterial(part, part.Material with { Textures = slots });
        }
        result = new() { Path = source.Path, Parts = parts, Warnings = source.Warnings, NoShapesInFile = source.NoShapesInFile };
        return true;
    }

    private static LodMeshPart WithMaterial(LodMeshPart p, LodMaterial material) => new()
    {
        Name = p.Name, TexGenScale = p.TexGenScale, Tier = p.Tier, Material = material,
        Positions = p.Positions, UVs = p.UVs, Normals = p.Normals, Tangents = p.Tangents,
        Bitangents = p.Bitangents, Colors = p.Colors, Triangles = p.Triangles
    };
}
