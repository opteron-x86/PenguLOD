using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.World;
using AnvilLOD.Meshes;

namespace AnvilLOD.Plugins;

/// <summary>Reads staged conversions before source assets, preserving Linux filename case.</summary>
internal sealed class ObjectTextureAssets(IAssetSource assets, string output)
{
    public byte[] Read(string path)
    {
        path = GamePath.NormalizeTexture(path);
        string? local = GamePath.ResolveExisting(output, path);
        if (local is not null) return File.ReadAllBytes(local);
        if (!assets.TryOpen(path, out var stream)) throw new FileNotFoundException("Texture not available: " + path);
        using (stream) { using var bytes = new MemoryStream(); stream.CopyTo(bytes); return bytes.ToArray(); }
    }

    public static bool IsLodTexture(LodMaterial material) => material.Textures.Count > 0
        && GamePath.NormalizeTexture(material.Textures[0]).StartsWith(@"textures\lod\", StringComparison.OrdinalIgnoreCase);

    public static string Root(LodMaterial material, string stage) =>
        (IsLodTexture(material) ? @"textures\lod\pengulod\" : @"textures\pengulod\") + stage + @"\";

    // NiAlphaProperty test function 4 is GREATER; 6 is GREATER_EQUAL. Blending remains untouched.
    public static int? AlphaCutoff(LodMaterial material)
    {
        if (!material.HasAlpha || (material.AlphaFlags & 0x201) != 0x200) return null;
        return ((material.AlphaFlags >> 10) & 7) switch { 4 => material.AlphaThreshold + 1, 6 => material.AlphaThreshold, _ => null };
    }

    public static LodMeshPart With(LodMeshPart p, LodMaterial material, System.Numerics.Vector2[]? uvs = null) => new()
    {
        Name = p.Name, TexGenScale = p.TexGenScale, Material = material, UVs = uvs ?? p.UVs,
        Positions = p.Positions, Normals = p.Normals, Tangents = p.Tangents, Bitangents = p.Bitangents,
        Colors = p.Colors, Triangles = p.Triangles, Tier = p.Tier
    };

    public static void Write(string output, string relative, byte[] bytes)
    {
        string path = GamePath.ToFileSystemPath(output, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Content-addressed files may already exist in seeded output. Repair damaged copies too.
        if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) File.WriteAllBytes(path, bytes);
        WrittenOutputHashes.Record(path, bytes);
    }
}
