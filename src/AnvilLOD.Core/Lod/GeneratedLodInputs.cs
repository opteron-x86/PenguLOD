using AnvilLOD.Core.World;

namespace AnvilLOD.Core.Lod;

/// <summary>Generated runtime placements and terrain support meshes are output, never ordinary source objects.</summary>
public static class GeneratedLodInputs
{
    public static bool IsPlugin(string name)
    {
        name = name.ToLowerInvariant();
        if (name is "anvillod.esp" or "anvillod.esm" or "pengulod.esp" or "pengulod.esm"
            or "anvillod underside.esp" or "anvillod underside.esm" or "anvillod effects.esp") return true;
        const string prefix = "anvillod effects ";
        return name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(".esp", StringComparison.Ordinal)
            && int.TryParse(name.AsSpan(prefix.Length, name.Length - prefix.Length - 4), out int chunk) && chunk >= 2;
    }

    public static bool IsModel(string path)
    {
        path = GamePath.Normalize(path);
        if (!path.StartsWith("meshes\\", StringComparison.Ordinal)) path = "meshes\\" + path;
        return path.StartsWith("meshes\\pengulod\\generated\\", StringComparison.Ordinal)
            || path.StartsWith("meshes\\terrain\\", StringComparison.Ordinal)
                && (path.EndsWith("_underside.nif", StringComparison.Ordinal) || path.EndsWith(".bto", StringComparison.Ordinal)
                    || path.EndsWith(".btr", StringComparison.Ordinal) || path.EndsWith(".btt", StringComparison.Ordinal));
    }
}
