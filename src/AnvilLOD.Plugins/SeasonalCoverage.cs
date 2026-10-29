using AnvilLOD.Core.Lod;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.World;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace AnvilLOD.Plugins;

public sealed record SeasonalSwapCoverage(string Season, string Category, int Entries, int Resolved, int Unresolved,
    IReadOnlyList<string> Problems);
public sealed record SeasonalOutputCoverage(string Season, int TerrainTiles, int ObjectBlocks, int TreeBlocks, int GrassCells);

internal static class SeasonalCoverage
{
    internal static bool Requested(GenerationPipelineRequest r) => r.Lod?.Seasons == true || r.Terrain?.Seasons == true
        || r.Grass is { Seasons: not "off" };

    internal static IReadOnlyList<SeasonalSwapCoverage> Inspect(GameContext game, AssetIndex assets, CancellationToken ct)
    {
        var groups = new Dictionary<(string Season,string Category),List<string?>>();
        foreach (string path in assets.EnumeratePaths(@"seasons\").Order(StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            if (SeasonSwaps.SeasonOf(GamePath.GetFileName(path)) is not { } season || !assets.TryOpen(path,out var stream)) continue;
            using var reader=new StreamReader(stream);
            foreach (var swap in SeasonSwaps.Parse(reader.ReadToEnd()))
            {
                string category=swap.Section.ToLowerInvariant();
                if (!Supported(category)) continue;
                var key=(season,category);
                if (!groups.TryGetValue(key,out var entries)) groups[key]=entries=[];
                entries.Add(Resolve(game,category,swap.Base) is not null && Resolve(game,category,swap.Swap) is not null
                    ? null : $"{path}: unresolved [{swap.Section}] {Label(swap.Base)} → {Label(swap.Swap)}");
            }
        }
        return groups.OrderBy(p=>p.Key.Season,StringComparer.Ordinal).ThenBy(p=>p.Key.Category,StringComparer.Ordinal)
            .Select(p=>new SeasonalSwapCoverage(p.Key.Season,p.Key.Category,p.Value.Count,p.Value.Count(v=>v is null),
                p.Value.Count(v=>v is not null),p.Value.OfType<string>().ToArray())).ToArray();
    }

    internal static bool Affects(GenerationPipelineRequest r,string category,bool seasonalGrass) => category switch
    {
        "landtextures" => r.Terrain?.Seasons == true || seasonalGrass,
        "trees" => r.Lod is {Seasons:true,Scan.TreeLod:true},
        "grasses" => seasonalGrass,
        _ => r.Lod?.Seasons == true
    };
    internal static bool Supported(string section) => section.ToLowerInvariant() is "trees" or "landtextures" or "grasses"
        or "statics" or "movablestatics" or "activators" or "furniture";

    internal static FormKey? Resolve(GameContext game,string section,SeasonFormRef form)
    {
        return section.ToLowerInvariant() switch
        {
            "trees" => Find<ITreeGetter>(), "landtextures" => Find<ILandscapeTextureGetter>(), "grasses" => Find<IGrassGetter>(),
            "statics" => Find<IStaticGetter>(), "movablestatics" => Find<IMoveableStaticGetter>(),
            "activators" => Find<IActivatorGetter>(), "furniture" => Find<IFurnitureGetter>(), _ => null
        };
        FormKey? Find<T>() where T:class,IMajorRecordGetter
        {
            if (form.Plugin is { } plugin)
                return SeasonalLod.PluginForm(plugin,form.LocalId) is { } key && game.LinkCache.TryResolve<T>(key,out _) ? key : null;
            return game.LinkCache.TryResolve<T>(form.EditorId!,out var record) ? record.FormKey : null;
        }
    }
    private static string Label(SeasonFormRef form) => form.EditorId ?? $"0x{form.LocalId:X}~{form.Plugin}";

    internal static IReadOnlyList<SeasonalOutputCoverage> Outputs(GenerationPipelineRequest request,CancellationToken ct)
    {
        var counts=new Dictionary<string,int[]>(StringComparer.Ordinal) { ["Default"]=new int[4] };
        foreach(var (suffix,_) in SeasonSwaps.Seasons) counts[suffix]=new int[4];
        foreach(var (root,kind) in new[]{(request.Terrain?.OutputFolder,0),(request.Lod?.OutputFolder,1),(request.Grass?.OutputFolder,3)})
        {
            if(root is null || !Directory.Exists(root))continue;
            foreach(var path in Directory.EnumerateFiles(root,"*",new EnumerationOptions {RecurseSubdirectories=true,AttributesToSkip=FileAttributes.ReparsePoint}))
            {
                ct.ThrowIfCancellationRequested();string name=Path.GetFileName(path);string ext=Path.GetExtension(name).ToLowerInvariant();
                int category=kind==0&&ext==".btr"?0:kind==1&&ext==".bto"?1:kind==1&&ext==".btt"?2:kind==3&&ext==".cgid"?3:-1;
                if(category<0)continue;
                string season=SeasonSwaps.Seasons.FirstOrDefault(s=>name.EndsWith("."+s.Suffix+ext,StringComparison.OrdinalIgnoreCase)).Suffix ?? "Default";
                counts[season][category]++;
            }
        }
        return counts.Select(p=>new SeasonalOutputCoverage(p.Key,p.Value[0],p.Value[1],p.Value[2],p.Value[3])).ToArray();
    }
}
