using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using AnvilLOD.Core;
using AnvilLOD.Core.Paths;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.World;
using AnvilLOD.Meshes.Nif;
using AnvilLOD.Plugins.Mo2;

namespace AnvilLOD.Plugins;

public sealed record DiagnosticIssue(string Severity,string Code,string Message,string Action,string? Asset=null,string? Source=null);
public sealed record DiagnosticEstimate(string Name,long Value,string Unit,string Basis);
public sealed record DiagnosticReport(string Application,string OperatingSystem,string GameRelease,string? GameVersion,
    DateTimeOffset CreatedAt,double MeasuredSeconds,int Plugins,int Archives,bool DeepScan,bool VerifiedOutputs,
    IReadOnlyList<DiagnosticIssue> Issues,IReadOnlyList<DiagnosticEstimate> Estimates,IReadOnlyList<Tree3DLevelReport> TreeCoverage,
    IReadOnlyList<SeasonalSwapCoverage>? Seasons = null)
{
    public bool CanGenerate => Issues.All(i=>i.Severity!="Error");
    public void Write(string path)
    { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);File.WriteAllText(path,JsonSerializer.Serialize(this,PersistentPipeline.Json)); }
}

/// <summary>Read-only pipeline checks. Reports contain metadata, never game assets or absolute source paths.</summary>
public static class PipelineDiagnostics
{
    public static DiagnosticReport Run(GenerationPipelineRequest request,bool deepScan=false,bool verifyOutputs=false,string? inspectAsset=null,
        IProgress<string>? progress=null,CancellationToken ct=default)
    {
        IReadOnlyList<SeasonalSwapCoverage> seasons=[]; bool seasonal=SeasonalCoverage.Requested(request);
        var clock=Stopwatch.StartNew();var issues=new List<DiagnosticIssue>();var estimates=new List<DiagnosticEstimate>();
        var source=request.Grass?.Game??request.Textures?.Game??request.Terrain?.Game??request.Lod?.Game??throw new ArgumentException("Select at least one pipeline stage.");
        var outputs=new[]{request.Grass?.OutputFolder,request.Textures?.OutputFolder,request.Terrain?.OutputFolder,request.Lod?.OutputFolder}.OfType<string>().ToArray();
        var redact=new ReportRedactor(source,outputs,ResourceCatalog.Folders(request.Textures?.DynDolodFolder, request.Textures?.ResourcePacks).Concat(ResourceCatalog.Folders(request.Lod?.DynDolodFolder, request.Lod?.ResourcePacks)).ToArray());
        void Add(string severity,string code,string message,string action,string? asset=null,string? origin=null)
        { issues.Add(new(severity,code,redact.Apply(message),redact.Apply(action),asset is null?null:redact.Apply(asset),origin is null?null:redact.Apply(origin))); }
        progress?.Report("Preflight: checking selected outputs and profile...");
        foreach(var output in outputs)
        {
            ct.ThrowIfCancellationRequested();
            foreach(var issue in Preflight.Check(source,output)) Add("Warning",issue.Code,issue.Message,"Review the selected profile and disable conflicting generated plugins.");
            if(Directory.Exists(output))
            {
                long bytes=0;
                foreach(var f in Directory.EnumerateFiles(output,"*",new EnumerationOptions{RecurseSubdirectories=true,AttributesToSkip=FileAttributes.ReparsePoint,IgnoreInaccessible=false}))
                {ct.ThrowIfCancellationRequested();bytes+=new FileInfo(f).Length;}
                estimates.Add(new("Existing "+redact.Apply(output),bytes,"bytes","Measured current output size; replacement and retained backups require additional space."));
                try
                {
                    var drive=DriveInfo.GetDrives().Where(d=>HostPath.IsWithin(output,d.Name)).OrderByDescending(d=>d.Name.Length).FirstOrDefault();
                    if(drive is not null)
                    {
                        estimates.Add(new("Free space for "+redact.Apply(output),drive.AvailableFreeSpace,"bytes","Filesystem free space at preflight time."));
                        if(request.Lod is {CleanOutput:false} lod && HostPath.Canonical(output)==HostPath.Canonical(lod.OutputFolder!) && drive.AvailableFreeSpace<bytes)
                            Add("Error","insufficient-staging-space","There is not enough free space to clone the existing LOD output into staging.","Free space or select another output filesystem.");
                    }
                }
                catch(IOException e){Add("Warning","free-space-unavailable",e.Message,"Check destination free space before generation.");}
            }
        }
        source=source with{ExcludeFolders=(source.ExcludeFolders??[]).Concat(outputs).Distinct().ToArray(),ExtraLooseRoots=(source.ExtraLooseRoots??[]).Concat(seasonal ? ["seasons","grass"] : Array.Empty<string>()).Concat(new[]{"dyndolod"}).Concat(deepScan || inspectAsset is {Length:>0} ? ["skse","textures"] : new[]{"skse"}).Distinct().ToArray()};
        int plugins=0,archives=0;string? version=null;AssetIndex assets;
        using(var game=GameContext.Open(source,progress))
        {
            plugins=game.PluginsLoaded;version=SksePluginInstaller.GameVersion(game.DataFolder)?.ToString();
            foreach(var missing in game.MissingPlugins)Add("Error","missing-plugin","Enabled plugin is unavailable: "+missing,"Restore the plugin or correct the profile before generating.",missing);
            foreach(var warning in game.Warnings)Add("Warning","source-warning",warning,"Check the selected game and profile paths.");
            assets=game.BuildAssets(p=>p.StartsWith("dyndolod\\") || p.StartsWith("skse\\") || seasonal && (p.StartsWith("seasons\\") || p.StartsWith("grass\\")) || deepScan && (p.StartsWith("meshes\\")||p.StartsWith("textures\\")) || inspectAsset is not null);
            archives=assets.ArchiveCount;
            foreach(var archive in assets.SkippedArchives)Add("Warning","archive-unreadable",archive,"Repair or remove the unreadable archive before generating.");
            foreach (var stage in new[] {
                (Name: "Textures", Folder: request.Textures?.DynDolodFolder, Packs: request.Textures?.ResourcePacks, Output: request.Textures?.OutputFolder),
                (Name: "LOD", Folder: request.Lod?.DynDolodFolder, Packs: request.Lod?.ResourcePacks, Output: request.Lod?.OutputFolder) }.Where(s => s.Output is not null))
            {
                try
                {
                    ResourceCatalog.ValidateFolders(stage.Folder, stage.Packs, stage.Output);
                    var resources = ResourceCatalog.Load(game, assets, stage.Folder, stage.Packs, ct);
                    estimates.Add(new(stage.Name + " resource files", resources.Report.Files.Count, "files", "Winning compatible rules, configurations and render models from resource folders and enabled mods."));
                    if (stage.Name == "Textures" && request.Textures?.Stitched == true && TextureGeneration.ReadRules(game, resources).Program.Entries.Count == 0)
                        Add("Error", "missing-texture-recipes", "No stitching recipes match the enabled plugins.", "Add a compatible resource pack or turn off stitched textures.");
                    if (stage.Name == "Textures" && request.Textures?.RenderedObjects == true && !resources.RenderModels("objects").Any())
                        Add("Error", "missing-render-models", "No object render models were found.", "Add a compatible resource pack or turn off rendered objects.");
                }
                catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
                { Add("Error", "invalid-resource-pack", stage.Name + ": " + e.Message, "Select a resource folder containing Rules, Configs or Render, or compatible data files."); }
            }
            bool seasonalGrass=request.Grass?.Seasons=="on" || request.Grass?.Seasons=="auto" && assets.Exists(@"skse\plugins\po3_seasonsofskyrim.dll");
            if(request.Lod?.Seasons==true || request.Terrain?.Seasons==true || seasonalGrass)
            {
                seasons=SeasonalCoverage.Inspect(game,assets,ct);
                foreach(var row in seasons)
                {
                    Add("Info","seasonal-swaps",$"{row.Season} {row.Category}: {row.Resolved}/{row.Entries} entries resolve.","Later INI entries win; identity entries restore the original record.");
                    foreach(string problem in row.Problems)
                        Add(SeasonalCoverage.Affects(request,row.Category,seasonalGrass)?"Error":"Warning","unresolved-seasonal-swap",problem,
                            "Correct the seasonal INI or restore its required plugin. Runtime-generated swaps must first be saved as INIs.");
                }
                if(seasons.Count==0)Add("Warning","no-seasonal-swaps","No supported written seasonal swaps were found.",
                    "Make the Seasons of Skyrim INIs available to this profile. Runtime automatic snow is not generated here.");
                if(request.Lod is {GrassLod:true,Seasons:true})
                {
                    var cacheCells=new Dictionary<string,HashSet<string>>(StringComparer.Ordinal);
                    foreach(string path in assets.EnumeratePaths(@"grass\"))
                    {
                        ct.ThrowIfCancellationRequested();
                        if(!GrassCache.TryParseFileName(GamePath.GetFileName(path),out var ws,out int x,out int y,out var season))continue;
                        if(!cacheCells.TryGetValue(season??"Default",out var cells))cacheCells[season??"Default"]=cells=[];
                        cells.Add($"{ws.ToLowerInvariant()}:{x}:{y}");
                    }
                    var baseline=cacheCells.GetValueOrDefault("Default")??[];
                    foreach(string season in seasons.Select(s=>s.Season).Concat(cacheCells.Keys.Where(k=>k!="Default")).Distinct())
                    {
                        var cells=cacheCells.GetValueOrDefault(season)??[];
                        int missing=baseline.Except(cells).Count(),orphan=cells.Except(baseline).Count();
                        Add(missing+orphan>0?"Warning":"Info","seasonal-grass-coverage",$"{season}: {cells.Count} seasonal grass cells; {missing} default cells without seasonal cache; {orphan} seasonal cells without default cache.",
                            request.Grass is not null?"The selected grass stage will rebuild caches. Review the final seasonal output counts.":"Generate grass caches for every intended season. Missing seasonal cells retain default grass; cells without a default cache cannot enter LOD.");
                    }
                }
            }
            if(request.Lod is {} lod)
            {
                bool runtime=lod.Scan.DynamicLod || lod.Scan.ParentCopies || lod.Scan.GlowWindows || lod.Scan.GridObjects&&!lod.NativeGridObjects;
                bool present=assets.Exists(SksePluginInstaller.RelativeDll);
                if(runtime && !present && lod.SkseDll==SkseDllChoice.None)
                    Add("Warning","runtime-plugin-missing","Selected dynamic LOD features need the AnvilLOD SKSE plugin. It is not present in the selected profile.",
                        "Install the Windows game plugin matching your Skyrim runtime; PenguLOD itself runs natively on Linux or Windows.",SksePluginInstaller.RelativeDll);
                if(present && assets.TryOpen(SksePluginInstaller.RelativeDll,out var dll))
                {
                    try
                    {
                        using(dll) using(var pe = new PEReader(dll))
                            if(pe.PEHeaders.PEHeader?.Magic != PEMagic.PE32Plus || pe.PEHeaders.CoffHeader.Machine != Machine.Amd64)
                                Add("Error","runtime-plugin-format","AnvilLOD.dll is not an x64 Windows PE module.","Install the matching game-side Windows SKSE plugin.",SksePluginInstaller.RelativeDll);
                    }
                    catch(BadImageFormatException) { Add("Error","runtime-plugin-format","AnvilLOD.dll is malformed or is not a Windows PE module.","Reinstall the matching game-side Windows SKSE plugin.",SksePluginInstaller.RelativeDll); }
                }
                if(runtime)Add("Info","runtime-version",version is null?"The game runtime version could not be determined.":"Detected Skyrim runtime "+version+".",
                    "Use the matching SKSE, address library and AnvilLOD game plugin. DLL presence alone does not establish version compatibility.");
                if(lod.Seasons && !assets.Exists(@"skse\plugins\po3_SeasonsOfSkyrim.dll"))
                    Add("Warning","seasons-runtime-missing","Seasonal LOD is selected but Seasons of Skyrim is not present in the profile.","Install and configure the matching seasonal runtime before testing seasonal output.");
                if(lod.CsGrassLod)
                    Add("Info","grass-runtime","Grass LOD still needs generated cache and billboards; shader support does not generate those files.","Use the compatible Grass Optimizations/LOD feature in Community or Bottled Shaders and test the grass transition distance.");
                if(lod.GeneratedGrassFolder is {} grass)
                    Check("grass-cache",()=> {var m=GrassGenerationManifest.Read(grass,true)!;m.ValidateLoadOrder(game);if(verifyOutputs)m.Verify(grass,progress,ct);},request.Grass is not null);
                if(lod.GeneratedTextureFolder is {} textures)
                    Check("texture-output",()=>{var m=TextureGenerationManifest.Read(textures,true)!;if(verifyOutputs)m.Verify(textures,progress,ct);},request.Textures is not null);
                if(lod.GeneratedTerrainFolder is {} terrain)
                    Check("terrain-output",()=>{var m=TerrainGeneration.ReadManifest(terrain);if(verifyOutputs)m.Verify(terrain,progress,ct);},request.Terrain is not null);
            }
            if(request.Terrain?.ExportHeightmaps==true)
                Add("Info","terrain-shadows","Terrain heightmaps are exported for the Terrain Shadows feature.","Enable compatible Community/Bottled Shaders Terrain Shadows and leave the generated heightmap DDS files loose.");
            if(inspectAsset is {Length:>0})
            {
                GamePath.ValidateRelative(inspectAsset);
                string path=GamePath.Normalize(inspectAsset);
                Add(assets.Exists(path)?"Info":"Warning","asset-precedence",assets.Exists(path)?"Winning asset: "+path:"Asset is not present: "+path,
                    "Loose files override archives; later enabled mods override earlier loose files; Overwrite has the final loose-file priority.",path,assets.DescribeOrigin(path));
            }
            void Check(string code,Action test,bool rebuilding)
            {
                try{ct.ThrowIfCancellationRequested();test();}
                catch(Exception e) when(e is not OperationCanceledException)
                {Add(rebuilding?"Warning":"Error","stale-"+code,e.Message,rebuilding?"The selected pipeline stage will regenerate this input.":"Regenerate this input or disable its use before LOD generation.");}
            }
        }
        var trees=new List<Tree3DLevelReport>();
        if(deepScan && request.Lod is {} scan && issues.All(i=>i.Severity!="Error"))
        {
            progress?.Report("Preflight: scanning LOD coverage without generating output...");
            var check=scan with{Game=source,Generate=false,CleanOutput=false,OutputFolder=null,
                GeneratedGrassFolder=verifyOutputs&&request.Grass is null?scan.GeneratedGrassFolder:null,
                GeneratedTextureFolder=verifyOutputs&&request.Textures is null?scan.GeneratedTextureFolder:null,
                GeneratedTerrainFolder=verifyOutputs&&request.Terrain is null?scan.GeneratedTerrainFolder:null,
                Scan=scan.Scan with{RenderTreeBillboards=false}};
            try
            {
                var summary=ScanPipeline.Run(check,progress,ct);
                foreach(var warning in summary.Warnings)Add("Warning","scan-warning",warning,"Review the relevant model, texture or profile setting.");
                if(summary.Grids.Count==0)Add("Error","no-worldspaces","No LOD worldspaces matched the selection.","Check EditorIDs and installed LODSettings files.");
                foreach(var missing in summary.Stats.MissingMeshes.OrderByDescending(p=>p.Value).Take(250))
                    Add("Warning","missing-lod-model",$"{missing.Value:N0} references lack this LOD model.","Install the appropriate LOD resource or generate a native model using Mod author/Mesh maker.",missing.Key);
                foreach(var missing in (summary.MissingBillboards??new Dictionary<string,long>()).Where(p=>summary.SkippedByTexGen?.Contains(p.Key)!=true).Take(250))
                    Add("Warning","missing-tree-billboard",$"{missing.Value:N0} trees have no billboard.",request.Textures is null?"Generate tree textures, then run the coverage check again.":"The selected texture stage may supply this billboard; check coverage again after it finishes.",missing.Key);
                foreach(var missing in (summary.MissingGrassBillboards??new Dictionary<string,long>()).Take(250))
                    Add("Warning","missing-grass-billboard",$"{missing.Value:N0} instances lack a grass billboard.","Generate grass billboards using the same profile.",missing.Key);
                trees.AddRange(summary.Tree3D?.Levels??[]);
                estimates.Add(new("LOD blocks",summary.Quads.Count,"blocks","Planned from the selected worldspaces and levels; pending texture/grass stages can change coverage."));
                estimates.Add(new("Placed object references",summary.Stats.LodReferencesFound,"references","Measured scan count, before block culling."));
                estimates.Add(new("Near tree geometry",summary.Tree3D?.Triangles??0,"triangles","Selected model cost times instances, before block culling; not an elapsed-time estimate."));
                // Open only distinct selected models, bounded to keep diagnostics practical on large profiles.
                var paths=summary.Quads.Values.SelectMany(v=>v).SelectMany(r=>new[]{r.Meshes.Lod4,r.Meshes.Lod8,r.Meshes.Lod16,r.Meshes.Lod32})
                    .OfType<string>().Where(p=>!p.StartsWith('~')).Distinct(StringComparer.OrdinalIgnoreCase).Take(1000).ToArray();
                progress?.Report($"Preflight: auditing texture references in {paths.Length:N0} selected models (limit 1,000)...");
                int missingTextures=0;
                foreach(var path in paths)
                {
                    ct.ThrowIfCancellationRequested();if(!assets.TryOpen(path,out var stream))continue;
                    try
                    {
                        byte[] bytes;using(stream){using var ms=new MemoryStream();stream.CopyTo(ms);bytes=ms.ToArray();}
                        var model=NifGeometryReader.Read(path,bytes,passthru:true);
                        if(model.HasIncompleteGeometry)Add("Warning","unsupported-model-geometry",string.Join("; ",model.Warnings),"Use a compatible static model or retain existing LOD; incomplete geometry will not be generated.",path,assets.DescribeOrigin(path));
                        foreach(var texture in model.Parts.SelectMany(p=>p.Material.Textures.Take(2)).Where(p=>!string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase))
                            if(!assets.Exists(texture) && missingTextures++<250)Add("Warning","missing-model-texture","Selected model references a missing texture: "+path,"Generate its LOD textures or install the required texture resource.",texture,assets.DescribeOrigin(path));
                    }
                    catch(Exception e) when(e is not OperationCanceledException){Add("Warning","model-audit-failed",e.Message,"Inspect or repair the source mesh.",path);}
                }
            }
            catch(Exception e) when(e is not OperationCanceledException){Add("Error","coverage-scan-failed",e.Message,"Correct the reported source/configuration problem and run diagnostics again.");}
        }
        progress?.Report($"Preflight completed in {clock.Elapsed.TotalSeconds:F1}s: {issues.Count(i=>i.Severity=="Error")} errors, {issues.Count(i=>i.Severity=="Warning")} warnings.");
        return new(ProductInfo.Description,RuntimeInformation.OSDescription,source.Release.ToString(),version,DateTimeOffset.UtcNow,
            clock.Elapsed.TotalSeconds,plugins,archives,deepScan,verifyOutputs,issues.Distinct().ToArray(),estimates,trees,seasons);
    }

    private sealed class ReportRedactor
    {
        private readonly (string Path,string Label)[] _paths;
        public ReportRedactor(GameContextOptions game,IReadOnlyList<string> outputs,IReadOnlyList<string> resources)
        {
            var paths=new List<(string?,string)>{(game.DataFolder,"<game>"),(game.Mo2InstanceFolder,"<instance>"),(game.PluginsTxt,"<plugins.txt>"),(game.WinePrefix,"<wine-prefix>"),
                (game.Mo2Locations?.ModsFolder,"<mods>"),(game.Mo2Locations?.ProfilesFolder,"<profiles>"),(game.Mo2Locations?.OverwriteFolder,"<overwrite>"),(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"<home>")};
            if (game.Mode == GameSourceMode.Mo2Instance && game.Mo2InstanceFolder is { } folder)
            {
                try
                {
                    var instance = Mo2Instance.Open(folder, game.Mo2Locations);
                    paths.AddRange([(instance.GamePath, "<game>"), (instance.ModsFolder, "<mods>"), (instance.ProfilesFolder, "<profiles>"), (instance.OverwriteFolder, "<overwrite>")]);
                }
                catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException) { }
            }
            paths.AddRange(outputs.Select((p,i)=>((string?)p,$"<output-{i+1}>")));
            paths.AddRange(resources.Select((p,i)=>((string?)p,$"<resource-{i+1}>")));
            _paths=paths.Where(p=>!string.IsNullOrWhiteSpace(p.Item1)).Select(p=>(p.Item1!,p.Item2)).OrderByDescending(p=>p.Item1.Length).ToArray();
        }
        public string Apply(string value)
        {foreach(var (path,label) in _paths)value=value.Replace(path,label,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal);return value;}
    }
}
