using AnvilLOD.Core.Lod;
using AnvilLOD.Core.World;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AnvilLOD.Plugins;

/// <summary>Winning named seasonal INIs. Replacement lookups are single-step, like the game plugin.</summary>
internal static class SeasonalAssets
{
    public static IReadOnlyDictionary<string, Dictionary<FormKey, FormKey>> Read(GameContext game, AssetIndex assets, string section)
    {
        var result = new Dictionary<string, Dictionary<FormKey, FormKey>>();
        foreach (var path in assets.EnumeratePaths(@"seasons\").Order(StringComparer.OrdinalIgnoreCase))
        {
            if (SeasonSwaps.SeasonOf(GamePath.GetFileName(path)) is not { } season || !assets.TryOpen(path, out var stream)) continue;
            using var reader = new StreamReader(stream);
            if (!result.TryGetValue(season, out var swaps)) result[season] = swaps = [];
            foreach (var swap in SeasonSwaps.Parse(reader.ReadToEnd()).Where(s => s.Section.Equals(section, StringComparison.OrdinalIgnoreCase)))
            {
                var from = SeasonalCoverage.Resolve(game,section,swap.Base); var to = SeasonalCoverage.Resolve(game,section,swap.Swap);
                if (from is null || to is null) throw new InvalidDataException($"{path}: unresolved {section} swap {swap.Base} → {swap.Swap}.");
                // Identity entries deliberately cancel an earlier file's replacement.
                swaps[from.Value] = to.Value;
            }
        }
        return result;

    }

    public static TreeReference SwapTree(GameContext game, AssetIndex assets, TreeReference tree, IReadOnlyDictionary<FormKey,FormKey> swaps, Tree3DSettings settings)
    {
        if (tree.BaseFormKey is null || !swaps.TryGetValue(FormKey.Factory(tree.BaseFormKey),out var target)
            || target.ToString().Equals(tree.BaseFormKey,StringComparison.OrdinalIgnoreCase)) return tree;
        if (!game.LinkCache.TryResolve<ITreeGetter>(target,out var record) || record.Model?.File is not { IsNull:false } model)
            throw new InvalidDataException("Seasonal tree has no usable model: " + target);
        string path = GamePath.Normalize(model.DataRelativePath.Path);
        if (!path.StartsWith(@"meshes\")) path = @"meshes\" + path;
        string stem = Tree3DModels.Stem(path) + $"_{target.ID:x8}";
        TreeBillboard? billboard = null;
        foreach (var plugin in game.LinkCache.ResolveAllContexts<ITree,ITreeGetter>(target).Select(c=>c.ModKey).Append(target.ModKey).Distinct())
        {
            string root = $@"textures\terrain\lodgen\{plugin.FileName.String}\{stem}";
            foreach (var (suffix,first) in new[]{(".dds",false),("_1.dds",true)})
            {
                if (!assets.Exists(root + suffix) || !assets.TryOpen(root + ".txt",out var stream)) continue;
                using var reader = new StreamReader(stream);
                billboard = TreeBillboard.Parse(root + suffix,reader.ReadToEnd(),first);
                if (billboard is not null) break;
            }
            if (billboard is not null) break;
        }
        if (billboard is null) throw new InvalidDataException($"Seasonal tree {target} has no billboard. Generate tree textures with the same profile first.");
        string? lod = null; bool native = false;
        if (settings.Enabled && tree.Model3D is not null)
        {
            string named = path[..^4] + "_lod_0.nif";
            if (settings.PreferNamedLod && assets.Exists(named)) lod = named;
            if (lod is null && assets.TryOpen(path,out var stream))
            {
                using(stream) { using var bytes = new MemoryStream();stream.CopyTo(bytes);
                    lod = Tree3DModels.Resolve(path,Tree3DModels.Crc32(bytes.ToArray()),settings.PlainNameFallback,assets.Exists)?.Path; }
            }
            if (lod is null && settings.PlainNameFallback && assets.Exists(named)) lod = named;
            if (lod is null && settings.GenerateMissing
                && assets.Exists(path)) {lod = path; native = true;}
            if (record.Model.AlternateTextures is { Count: > 0 } && settings.GenerateMissing && assets.Exists(path))
            { lod = path; native = true; }
        }
        return tree with {BaseFormKey=target.ToString(),Billboard=billboard,Model3D=lod,Model3DGenerated=native,Model3DByName=false,
            MaterialVariant=TreeMaterials.Read(game,record.Model)};
    }
}
