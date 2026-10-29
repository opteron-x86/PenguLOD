using System.Security.Cryptography;
using System.Text.Json;
using AnvilLOD.Core.World;

namespace AnvilLOD.Core.Lod;

/// <summary>Record-specific texture slots for a shape in a tree's full model.</summary>
public sealed record TreeTextureOverride(string ShapeName, int ShapeIndex, IReadOnlyList<string?> Slots);

public sealed record TreeMaterialVariant(string Id, string FullModel,
    IReadOnlyList<TreeTextureOverride> Overrides, string? Error = null)
{
    public static TreeMaterialVariant Create(string model, IEnumerable<TreeTextureOverride> overrides, string? error = null)
    {
        model = GamePath.Normalize(model);
        if (!model.StartsWith(@"meshes\", StringComparison.Ordinal)) model = @"meshes\" + model;
        var entries = overrides.Select(o => new TreeTextureOverride(o.ShapeName, o.ShapeIndex,
            o.Slots.Select(s => string.IsNullOrWhiteSpace(s) ? null : GamePath.NormalizeTexture(s)).ToArray())).ToArray();
        var identity = JsonSerializer.SerializeToUtf8Bytes(new { model, entries, error });
        return new(Convert.ToHexString(SHA256.HashData(identity)), model, entries, error);
    }
}
