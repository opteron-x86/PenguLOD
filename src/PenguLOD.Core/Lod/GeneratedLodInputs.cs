using PenguLOD.Core.World;

namespace PenguLOD.Core.Lod;

/// <summary>Generated runtime placements and terrain support meshes are output, never ordinary source objects.</summary>
public static class GeneratedLodInputs
{
    public static bool IsPlugin(string name)
    {
        name = name.ToLowerInvariant();
        if (name.StartsWith("anvillod", StringComparison.Ordinal)) name = "pengulod" + name[8..];
        if (name is "pengulod.esp" or "pengulod.esm"
            or "pengulod underside.esp" or "pengulod underside.esm" or "pengulod effects.esp") return true;
        const string prefix = "pengulod effects ";
        return name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(".esp", StringComparison.Ordinal)
            && int.TryParse(name.AsSpan(prefix.Length, name.Length - prefix.Length - 4), out int chunk) && chunk >= 2;
    }

    public static bool IsModel(string path)
    {
        path = GamePath.Normalize(path);
        if (!path.StartsWith("meshes\\", StringComparison.Ordinal)) path = "meshes\\" + path;
        return path.StartsWith("meshes\\pengulod\\generated\\", StringComparison.Ordinal)
            || path.StartsWith("meshes\\anvillod\\generated\\", StringComparison.Ordinal)
            || path.StartsWith("meshes\\terrain\\", StringComparison.Ordinal)
                && (path.EndsWith("_underside.nif", StringComparison.Ordinal) || path.EndsWith(".bto", StringComparison.Ordinal)
                    || path.EndsWith(".btr", StringComparison.Ordinal) || path.EndsWith(".btt", StringComparison.Ordinal));
    }
}
