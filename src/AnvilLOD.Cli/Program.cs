using System.Diagnostics;
using System.Text.Json;
using AnvilLOD.Core;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.World;
using AnvilLOD.Plugins;
using AnvilLOD.Desktop.Models;
using AnvilLOD.Desktop.Services;

namespace AnvilLOD.Cli;

public static class Program
{
    private static CancellationToken Cancellation;
    private const string Usage = """
        PenguLOD — native Linux and Windows LOD generator for Skyrim SE/AE

        Usage:
          PenguLOD pipeline --settings <preset.json> [--stages grass,textures,terrain,lod]
                                         Uses exactly the desktop preset schema and validation.
          PenguLOD grass|texgen|terrain|generate --settings <preset.json>
                                         Run one selected stage with persistent checkpoints.
          PenguLOD export-job --settings <preset.json> --output <request.json> [--stages ...]
          PenguLOD job-status --job-folder <saved-job-folder>
          PenguLOD compute-info [--allow-software] [--report <compute.json>]
          PenguLOD pipeline --request <request.json> [--cache-folder <folder>] [--no-resume] [--verify-source-contents]
                                         Run/resume a saved pipeline plan from the desktop's Open saved job folder.
                                         Completed stages are content-verified before reuse; failures stop dependent stages.
                                         Optional: --texture-compute Cpu|Auto|Vulkan --no-preflight --report <run.json>
          PenguLOD diagnose --request <request.json> --report <diagnostics.json>
                                         Check a pipeline without generating. Optional: --deep-scan --verify-outputs
                                         --inspect-asset <Data-relative-path>. Export contains metadata, not game assets.
          PenguLOD scan [options]        analyse the load order and show what would be built
          PenguLOD generate [options]    scan, then write the changed LOD blocks (.bto) to --output
          PenguLOD author --plugin <file> --author-out <folder> [options]
                                         Mod author tools: report the plugin's objects and trees that have no
                                         LOD, and generate LOD meshes, tree billboards and a rule file for them
                                         into their own folder. Options: --no-meshes --no-billboards --no-rules
                                         --min-size <units, default 400> --min-tree-height <units, default 256>
                                         --overwrite (replace files already in --author-out)
                                         --budget recommended|off|<LOD0,LOD1,LOD2> triangle budget for the LOD meshes (default
                                         recommended, by model size; see lodmaker)

          PenguLOD fix-textures (--input <folder> [--input ...] | --mo2 <instance> [--profile <name>]) [options]
                                         Finds textures Skyrim SE cannot load (BC6H, a format that crashes the game when a shader
                                         samples it) and converts them to BC7. Without --apply it only lists them. Options:
                                         --apply  --output <folder> (put the fixed files there, same relative paths: a new mod that
                                         overrides the broken ones) or --in-place --backup <folder> (replace them; the originals are
                                         copied to the backup folder first). Native managed conversion is the default.
                                         --texconv <executable> optionally uses an external converter instead
                                         --mo2 scans every enabled mod of the profile and Overwrite.

          PenguLOD convert-lod --source <mod folder> --output <folder> [--all-meshes] [--overwrite]
                                         Skyrim LE (Oldrim) -> SE: converts the static LOD meshes of a mod folder (loose files and its BSAs)
                                         to Skyrim SE meshes under the same paths in --output. Collision is dropped; animated, skinned and
                                         particle meshes are left alone. --all-meshes converts every mesh, not just LOD ones.

          PenguLOD lodmaker --input <file|folder> [--input ...] (--output <folder> | --mo2 <instance> --new-mod <name>) [options]
                                         LOD Mesh Maker: make LOD meshes (name_lod_0/1/2.nif, simplified, no collision, the
                                         model's own textures) from full models, plus a DynDOLOD-format rule file.
                                         --input is a .nif, a mod folder, its meshes folder or any folder of models.
                                         --new-mod <name> creates the mod folder in the MO2 mods folder (needs --mo2).
                                         Options: --group <name> (meshes\lod\<name>, default the output folder's name)
                                         --plugin <file> the plugin that places the models (e.g. TMDDirtRoads.esp): the rule
                                         file is named for it (DynDOLOD_SSE_tmddirtroadsesp.ini), the only name it is read under
                                         --levels 0,1,2 (LOD4, LOD8, LOD16; default all) --detail <0.1-10, default 1;
                                         lower keeps more detail> --min-size <units> --no-rules --overwrite
                                         --budget recommended|off|<LOD0,LOD1,LOD2>  triangle budget per model: the default,
                                         "recommended", scales with the model's size (from real SE/AE LOD meshes);
                                         "off" has none; three numbers set the maximums (0 = no limit), e.g. 800,500,300

        Options:
          --version             Show PenguLOD and upstream engine versions
          --mo2 <path>           MO2 instance folder (with ModOrganizer.ini) — reads mods directly, no need to launch from MO2
          --wine-prefix <path>   Wine/Proton pfx folder used to resolve Windows drive paths
          --game-ini <path>      Override Skyrim archive settings explicitly
          --cache-folder <path>  Override the generated tree-billboard cache location
          --threads <n>          Worker limit (0 = available processors minus one)
          --profile <name>       MO2 profile (default: the instance's selected profile)
          --data <path>          Skyrim Data folder (default: auto-detect / MO2 VFS)
          --plugins <path>       plugins.txt to use (requires --data)
          --worldspace <edid>    Limit to a worldspace; repeatable (default: all with a .lod file)
          --output <path>        Output folder (default for generate: "AnvilLOD Output" in the MO2 mods folder, Vortex's staging
                                 folder or Documents). It is created if missing and, if it is AnvilLOD's own (empty, or holds its
                                 manifest or log), emptied before every generate so old files never mix with new ones
          --keep-output          Don't empty the output folder first: update it in place (only changed blocks are rewritten)
          --report <file.json>   Write a JSON report (default: AnvilLOD.scan.json next to the exe)
          --dyndolod <path>      DynDOLOD install folder: reuse its LOD rule files (optional)
          --preset <name>        low | medium | high (default high): which rule preset to use
          --candles              Also load DynDOLOD's Candles rules (candles, lanterns and sconces get Far LOD); needs --dyndolod
          --fxglow               Also load DynDOLOD's FXGlow rules (fire and light glow cards get Far LOD); needs --dyndolod
          --rules <file>         Your own LOD rule file (DynDOLOD rule format, e.g. saved by the app's rule editor). Its rules come
                                 before all others; without it only DynDOLOD's own rule files are used
          --keep-buried          Don't remove LOD triangles buried under the terrain
          --keep-underwater      Don't remove LOD triangles that lie completely under water
          --include-disabled     Include initially-disabled references
          --no-child-worlds      Don't copy walled cities (Whiterun, Solitude, ...) into Tamriel's LOD
          --no-dynamic           Keep switchable references (quest-toggled) in static LOD instead of
                                 handing them to the AnvilLOD SKSE plugin
          --seasons              Seasonal object and grass LOD for Seasons of Skyrim (Data\Seasons\*_WIN.ini ...)
          --skse-dll <which>     SKSE plugin put in the output: auto (default, reads SkyrimSE.exe),
                                 1170 (SE 1.5.97 - AE 1.6.1170), 17 (newer, 1.7.x), none (installed separately)
          --no-grid-objects      Don't hand water planes, waterfalls, fires and windmills (DynDOLOD
                                 "grid" objects) to the AnvilLOD SKSE plugin
          --glow-windows         (experimental) DynDOLOD's window glow: the <model>glow_lod_N.nif meshes of farmhouses, inns and forts are
                                 drawn by the SKSE plugin beyond the loaded cells and lit at night (AnvilLOD.dyn). Off by default
          --parent-copies        (experimental) DynDOLOD's Parent > Child copies: the objects around Whiterun, Solitude, Windhelm and
                                 Riften that the walled-city worldspaces leave out are drawn by the SKSE plugin while you are in the
                                 city (AnvilLOD.copies). Needs --dyndolod for its childworld configs. Off by default
          --no-match-textures    Keep each LOD mesh's own textures. By default they follow the full model
                                 (alternate textures, snow/ice variants), the way DynDOLOD corrects them
          --no-grass             Don't build grass LOD from the grass cache
          --water-standins       Stream/creek/pond water planes (grid objects) that use a real water shader, which
                                 draws black beyond the loaded cells, get a lit fake-water stand-in mesh in the output
          --grass-density <pct>  Share of cached grass kept as LOD, 1-100 (default 8; 15 is high, 100 keeps every blade)
          --grass-top <pct>      Grass LOD brightness at the top of a tuft, 20-120 (default 85), like DynDOLOD's GrassBrightnessTop
          --grass-bottom <pct>   Grass LOD brightness at the roots, 10-120 (default 50), like GrassBrightnessBottom
          --tree-brightness <pct> Tree LOD billboard brightness, 10-110 (default 100)
          --object-brightness <pct> Object LOD brightness, 10-110 (default 100)
          --esl-range <auto|2048|4096>  Records per ESL effects plugin: auto reads the installed game (4096 on 1.6.1130 or newer, else 2048)
          --native-grid-objects     Waterfalls, windmill fans and water wheels (and Animated Ships' sailing ships) as real references in AnvilLOD.esp, which the engine animates
          --native-effects          Fires, candles, torches, lava and streams as real references too (second plugin, AnvilLOD Effects.esp)
          --no-cs-grass             Don't make LOD meshes for Community Shaders' Grass Optimizations (meshes\LOD\Grass; made only when the feature is installed)
          --no-animated-ships       Don't draw Animated Ships' sailing ships in the distance (they need the native grid objects; on by default when the mod is in the load order)
          --no-detailed-ships       Draw moored ships from the stock LOD meshes instead of simplified copies of their real models
          --ice-inset <units>       Pull glacier/ice LOD this far inside its surface so the full model wins the z-fight (default 6, 0 = off)
          --no-mountain-fit         Keep mountain LOD where its mesh has it (default: LOD4/LOD8 vertices outside the full mountain model are moved onto its surface, when a mountain inset is on)
          --mountain-inset <units>  The same for mountain LOD (default 12, 0 = off)
          --snow-cover <off|full|bright>  Vertex colour of snow/ash LOD shapes: full (default, alpha 1 = full snow intensity), off (as the meshes carry it), bright (full and lighter colour)
          --snow-shader <auto|plain|hd>  Snow/ash LOD shader on full-texture shapes: auto (default, DynDOLOD: HD at LOD4 only), plain (the plain shader at every level, so near mountains match far ones), hd (HD at every level)
          --object-inset <units>    The same for every other object in LOD4/LOD8 (buildings, walls, rocks; default 0 = off): smooths the swap to the full model
          --map-objects <mode>      LOD 32 objects for the world map: auto (default; level 0 when A Clear Map of Skyrim is loaded, else rules only), rules, 0, 1 or 2 (that level's LOD mesh)
          --no-map-trees            With --tree-3d: no tree cards in LOD 32, so no trees on the world map (LOD 4 to 16 unchanged)
          --occlusion               Work out the occlusion data (Occlusion.esp) ourselves instead of running xLODGen (EXPERIMENTAL)
          --occlusion-clearance <u> Units a hill must stand above the line of sight to hide what is behind it (default 3072; larger = safer, hides less)
          --occlusion-folder <dir>  Where Occlusion.esp goes (default: the output folder; also works with scan)
          --no-trees             Don't build billboard tree LOD (.lst/.btt + atlas)
          --render-trees         Trees without a TexGen billboard get one drawn from their own model, so TexGen is not needed
                                 for them (tall trees only; small plants TexGen skipped on purpose stay without LOD).
                                 Billboards are cached in the user cache folder (or --cache-folder); a real TexGen one wins.
          --tree-3d              Use 3D tree LOD models (DynDOLOD passthru_lod.nif, matched by the CRC32 of the
                                 tree's mesh) in object LOD: 3D at LOD4, billboard cards from LOD8 on.
                                 Trees without a model keep billboard tree LOD.
          --tree-3d-lod8         With --tree-3d: use a distinct/simpler model at LOD8 when it fits the budget
          --tree-near-budget N  Triangles per near tree model, default 4000; 0 removes the cap
          --tree-far-budget N   Triangles per far tree model, default 1600
          --tree-near-error N   Maximum trunk simplification deviation, default 4 game units
          --tree-far-error N    Maximum far-trunk deviation, default 24 game units
          --no-native-trees     Do not build missing 3D tree models from full tree meshes
          --tree-3d-crc-only     With --tree-3d: only use models whose file name carries the tree mesh's CRC32.
                                 Default: when none matches, the model stored under the plain tree name is used
                                 (how DynDOLOD Resources ships almost all of its models, and how DynDOLOD matches)
          --tree-3d-dyndolod-first
                                 With --tree-3d: use a DynDOLOD-style model (CRC32 or plain name) before a tree mod's own LOD
                                 model (<tree>_LOD_0.nif), as DynDOLOD does. Default: the mod's own LOD model wins when
                                 it has one (it needs its TexGen trunk billboards); trees without one keep the other kind.
          --mo2-game <dir>       With --mo2: the game folder (SkyrimSE.exe), instead of the gamePath in ModOrganizer.ini
          --mo2-mods <dir>       With --mo2: the mods folder, instead of the ini's (for lists on another drive)
          --mo2-profiles <dir>   With --mo2: the profiles folder, instead of the ini's
          --mo2-overwrite <dir>  With --mo2: the overwrite folder, instead of the ini's
                                 (Give the game, mods and profiles folders and ModOrganizer.ini isn't needed.)
          --large-refs           List the references that qualify for the engine's large reference grid but that no plugin
                                 lists yet in AnvilLOD.esm (flagged ESL). Enable it after your other ESMs
          --large-refs-no-esl    With --large-refs: don't flag AnvilLOD.esm as ESL; it is then a normal ESM that uses a
                                 plugin slot (try it if large references flicker and you want to rule the ESL flag out)
          --pbr-lod              Make object LOD textures match PBR full models: use TexGen's pbr_lod twins and
                                 convert PBR albedo copies (textures\anvillod\pbr) for LOD meshes that use a texture
                                 the full model replaces with textures\pbr\...
          --pbr-lod-brightness <pct> Brightness of the converted copies, 10-150 (default 100 = DynDOLOD's PBR scale 0.65)
          --pbr-lod-size <px>    Largest side of a converted copy: 256, 512 (default), 1024 or 2048
          --underside            Build the terrain underside for volumetric lighting mods (DVLaSS, EVLaS, Community
                                 Shaders sky sync): meshes\Terrain\<ws>\<ws>_Underside.nif plus AnvilLOD.esp
                                 (flagged ESL, no plugin slot). Enable the ESM; load it after plugins that edit worldspaces
          --underside-detail <n> LAND vertices per underside quad: 4, 8, 16 (default), 32, 64 or 128. Smaller is finer and heavier
          --no-enable-parented   Exclude every reference with an enable parent (by default only
                                 parented refs that start disabled are left out)

        Example: AnvilLOD scan --mo2 "D:\Modlists\MyList" --worldspace Tamriel
        """;

    /// <summary>Refuses options that would otherwise be silently ignored.</summary>
    private static CliArgs Checked(CliArgs a)
    {
        a.ResolvePaths();
        _ = Mo2LocationsFromArgs(a);
        return CheckedLargeRefs(a);
    }

    private static AnvilLOD.Plugins.Mo2.Mo2Locations? Mo2LocationsFromArgs(CliArgs a)
    {
        var l = new AnvilLOD.Plugins.Mo2.Mo2Locations(a.Get("mo2-game"), a.Get("mo2-mods"), a.Get("mo2-profiles"), a.Get("mo2-overwrite"));
        if (l.IsEmpty) return a.Get("mo2") is not null && a.Get("wine-prefix") is { } prefix ? l with { WinePrefix = prefix } : null;
        if (a.Get("mo2") is null) throw new CliArgException("--mo2-game, --mo2-mods, --mo2-profiles and --mo2-overwrite only apply together with --mo2 <instance folder>.");
        return l with { WinePrefix = a.Get("wine-prefix") };
    }

    private static CliArgs CheckedLargeRefs(CliArgs a)
    {
        if (a.Has("large-refs-no-esl") && !a.Has("large-refs"))
            throw new CliArgException("--large-refs-no-esl only applies together with --large-refs.");
        return a;
    }

    private static int PbrLodSizeFromArgs(CliArgs a)
    {
        if (a.Get("pbr-lod-size") is not { } v) return 512;
        if (!a.Has("pbr-lod") && a.Get("pbr-lod-brightness") is null) throw new CliArgException("--pbr-lod-size only applies together with --pbr-lod.");
        if (!int.TryParse(v, out var px) || px is not (256 or 512 or 1024 or 2048))
            throw new CliArgException($"--pbr-lod-size must be 256, 512, 1024 or 2048 (got '{v}').");
        return px;
    }

    private static int UndersideStepFromArgs(CliArgs a)
    {
        if (a.Get("underside-detail") is not { } v) return AnvilLOD.Core.World.UndersideMesher.DefaultStep;
        if (!a.Has("underside"))
            throw new CliArgException("--underside-detail only applies together with --underside.");
        if (!int.TryParse(v, out var step) || !AnvilLOD.Core.World.UndersideMesher.IsValidStep(step))
            throw new CliArgException($"--underside-detail must be 4, 8, 16, 32, 64 or 128 (got '{v}').");
        return step;
    }

    private static Tree3DSettings? Tree3DFromArgs(CliArgs a)
    {
        if (!a.Has("tree-3d"))
        {
            if (a.Has("tree-3d-lod8") || a.Has("tree-3d-by-name") || a.Has("tree-3d-crc-only") || a.Has("tree-3d-own-lod") || a.Has("tree-3d-dyndolod-first") || a.Has("no-native-trees") || a.Has("no-map-trees") || a.Has("tree-near-budget") || a.Has("tree-far-budget") || a.Has("tree-near-error") || a.Has("tree-far-error"))
                throw new CliArgException("--tree-3d-lod8, --tree-3d-by-name, --tree-3d-crc-only and --tree-3d-dyndolod-first only apply together with --tree-3d.");
            return null;
        }
        int Budget(string key, int fallback) => a.Get(key) is { } value && int.TryParse(value, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out int result) ? result : a.Has(key) ? throw new CliArgException("Invalid --" + key) : fallback;
        float Error(string key, float fallback) => a.Get(key) is { } value && float.TryParse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float result) ? result : a.Has(key) ? throw new CliArgException("Invalid --" + key) : fallback;
        var settings = new Tree3DSettings(Enabled: true, Lod8: a.Has("tree-3d-lod8"), PlainNameFallback: !a.Has("tree-3d-crc-only"),
            MapTrees: !a.Has("no-map-trees"), PreferNamedLod: !a.Has("tree-3d-dyndolod-first"), NearTriangleBudget: Budget("tree-near-budget", 4000),
            FarTriangleBudget: Budget("tree-far-budget", 1600), NearMaxError: Error("tree-near-error", 4), FarMaxError: Error("tree-far-error", 24), GenerateMissing: !a.Has("no-native-trees"));
        settings.Validate(); return settings;
    }

    public static int Main(string[] args)
    {
        if (args is ["--version"]) { Console.WriteLine(ProductInfo.Description); return 0; }
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(Usage);
            return args.Length == 0 ? 1 : 0;
        }

        using var cancellation = new CancellationTokenSource();
        Cancellation = cancellation.Token;
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); Console.Error.WriteLine("Cancelling…"); };
        Console.CancelKeyPress += cancel;
        try
        {
            var options = Checked(CliArgs.Parse(args[1..]));
            int threads = 0;
            if (options.Get("threads") is { } value && (!int.TryParse(value, out threads) || threads < 0 || threads > 1024))
                throw new CliArgException("--threads must be a whole number from 0 to 1024.");
            using var limits = RunLimits.Enter(threads, Cancellation, options.Get("cache-folder"));
            return args[0].ToLowerInvariant() switch
            {
                "compute-info" => RunComputeInfo(options),
                "pipeline" => RunPipeline(options),
                "grass" => RunPipeline(options,"grass"),
                "texgen" or "textures" => RunPipeline(options,"textures"),
                "terrain" => RunPipeline(options,"terrain"),
                "export-job" => ExportJob(options),
                "job-status" => JobStatus(options),
                "diagnose" => RunDiagnose(options),
                "scan" => RunScan(options, generate: false),
                "generate" => options.Has("request") || options.Has("settings") ? RunPipeline(options,"lod") : RunScan(options, generate: true),
                "author" => RunAuthor(options),
                "lodmaker" => RunLodMaker(options),
                "convert-lod" => RunConvertLod(options),
                "fix-textures" => RunFixTextures(options),
                _ => Fail($"Unknown command '{args[0]}'.\n\n{Usage}"),
            };
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        { Console.Error.WriteLine("Cancelled. Partial output may remain; rerun before installing it."); return 130; }
        catch (CliArgException e) { return Fail(e.Message + "\n\n" + Usage); }
        catch (Exception e) { return Fail(e.GetBaseException().Message); }
        finally { Console.CancelKeyPress -= cancel; }
    }

    private static int RunComputeInfo(CliArgs options)
    {
        var report = AnvilLOD.Textures.TextureCompute.Benchmark(options.Has("allow-software"));
        var json = JsonSerializer.Serialize(report, PersistentPipeline.Json);
        Console.WriteLine(json);
        if (options.Get("report") is { } file) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!); File.WriteAllText(file,json); }
        return options.Has("allow-software") && !report.QualityPassed ? 1 : 0;
    }

    private static int RunDiagnose(CliArgs options)
    {
        var request = LoadPipelineRequest(options);
        string path = options.Get("report") ?? throw new CliArgException("--report is required.");
        var report = PipelineDiagnostics.Run(request, options.Has("deep-scan"), options.Has("verify-outputs"), options.Get("inspect-asset"),
            new Progress<string>(Console.WriteLine), Cancellation);
        report.Write(path); Console.WriteLine("Diagnostic report: " + path);
        return report.CanGenerate ? 0 : 1;
    }

    private static GenerationPipelineRequest LoadPipelineRequest(CliArgs a, string? stage = null)
    {
        if (a.Has("request") == a.Has("settings")) throw new CliArgException("Choose exactly one of --request or --settings.");
        var selected = (stage ?? a.Get("stages"))?.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
        if(selected is not null && (selected.Length==0 || selected.Any(s=>s is not ("grass" or "textures" or "terrain" or "lod"))))
            throw new CliArgException("--stages accepts grass,textures,terrain,lod.");
        GenerationPipelineRequest request;
        if(a.Get("settings") is { } preset)
        {
            var settings=SettingsStore.Read(preset);
            if(selected is not null)
            {settings.PipelineGrass=selected.Contains("grass");settings.PipelineTextures=selected.Contains("textures");settings.PipelineTerrain=selected.Contains("terrain");settings.PipelineLod=selected.Contains("lod");}
            request=RequestFactory.Pipeline(settings);
        }
        else
        {
            request=JsonSerializer.Deserialize<GenerationPipelineRequest>(File.ReadAllText(a.Get("request")!),PersistentPipeline.Json)
                ?? throw new CliArgException("The pipeline request is empty.");
            if(selected is not null)
            {
                foreach(var name in selected)
                    if(name switch {"grass"=>request.Grass is null,"textures"=>request.Textures is null,"terrain"=>request.Terrain is null,_=>request.Lod is null})
                        throw new CliArgException("The request does not define stage: "+name);
                request=request with {Grass=selected.Contains("grass")?request.Grass:null,Textures=selected.Contains("textures")?request.Textures:null,
                    Terrain=selected.Contains("terrain")?request.Terrain:null,Lod=selected.Contains("lod")?request.Lod:null};
            }
        }
        return request with {Resume=!a.Has("no-resume")&&request.Resume,VerifySourceContents=a.Has("verify-source-contents")||request.VerifySourceContents,
            Preflight=!a.Has("no-preflight")&&request.Preflight,TextureCompute=a.Get("texture-compute")??request.TextureCompute};
    }

    private static string CacheFolder(CliArgs a)
    {
        if(a.Get("cache-folder") is { } explicitCache)return explicitCache;
        if(a.Get("settings") is { } preset && SettingsStore.Read(preset) is {CacheFolder.Length:>0} settings)
            return RequestFactory.PathValue(settings.CacheFolder,settings);
        return SettingsStore.CacheRoot;
    }
    private static int RunPipeline(CliArgs a,string? stage=null)
    {
        var request=LoadPipelineRequest(a,stage);
        int threads=a.Has("threads")?int.Parse(a.Get("threads")!):a.Get("settings") is { } preset?(int)SettingsStore.Read(preset).MaxThreads:0;
        using var limits=RunLimits.Enter(threads,Cancellation,CacheFolder(a));
        var result=GenerationPipeline.Run(request,CacheFolder(a),new SyncProgress(Console.WriteLine),Cancellation);
        if(a.Get("report") is { } file)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
            File.WriteAllText(file,JsonSerializer.Serialize(new{result.Success,result.FailedStage,result.Error,result.JobFolder,result.Journal,result.LodReceipt,result.Diagnostics},PersistentPipeline.Json));
        }
        Console.WriteLine("Saved job: "+result.JobFolder);
        return result.Success?0:Fail(result.Error!);
    }
    private static int ExportJob(CliArgs a)
    {
        var request=LoadPipelineRequest(a);
        string file=a.Get("output")??throw new CliArgException("--output <request.json> is required.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        File.WriteAllText(file,JsonSerializer.Serialize(request,PersistentPipeline.Json));
        Console.WriteLine("Pipeline request: "+file);return 0;
    }
    private static int JobStatus(CliArgs a)
    {
        string folder=a.Get("job-folder")??throw new CliArgException("--job-folder is required.");
        var journal=JsonSerializer.Deserialize<PipelineJournal>(File.ReadAllText(Path.Combine(folder,"job.json")),PersistentPipeline.Json)
            ??throw new InvalidDataException("Empty pipeline journal.");
        Console.WriteLine(JsonSerializer.Serialize(journal,PersistentPipeline.Json));return 0;
    }

    private static int RunScan(CliArgs a, bool generate)
    {
        var total = Stopwatch.StartNew();
        var gameOptions = a.Get("mo2") is { } mo2Folder
            ? new GameContextOptions(Mode: GameSourceMode.Mo2Instance, Mo2InstanceFolder: mo2Folder, Mo2Profile: a.Get("profile"), Mo2Locations: Mo2LocationsFromArgs(a), GameIniPath: a.Get("game-ini"), WinePrefix: a.Get("wine-prefix"))
            : new GameContextOptions(a.Get("data"), a.Get("plugins"), GameIniPath: a.Get("game-ini"), WinePrefix: a.Get("wine-prefix"));
        var outputFolder = a.Get("output") ?? (generate ? OutputFolder.DefaultFolder(gameOptions) : null);
        if (generate && a.Get("output") is null) Console.WriteLine($"No --output given: using {outputFolder}");

        var req = new ScanRequest(
            gameOptions,
            new ScanOptions(
                Worldspaces: a.GetAll("worldspace"),
                IncludeInitiallyDisabled: a.Has("include-disabled"),
                IncludeEnableParented: !a.Has("no-enable-parented"),
                TreeLod: !a.Has("no-trees"),
                DynamicLod: !a.Has("no-dynamic"),
                GridObjects: !a.Has("no-grid-objects"),
                Tree3D: Tree3DFromArgs(a),
                MatchTextures: !a.Has("no-match-textures"),
                ParentCopies: a.Has("parent-copies"),
                GlowWindows: a.Has("glow-windows"),
                RenderTreeBillboards: a.Has("render-trees")),
            OutputFolder: outputFolder,
            CleanOutput: !a.Has("keep-output"),
            Generate: generate,
            DynDolodFolder: a.Get("dyndolod"),
            Preset: ParsePreset(a.Get("preset")),
            Candles: a.Has("candles"),
            FxGlow: a.Has("fxglow"),
            CustomRulesFile: a.Get("rules"),
            RemoveBuried: !a.Has("keep-buried"),
            RemoveUnderwater: !a.Has("keep-underwater"),
            GrassLod: !a.Has("no-grass"),
            ChildWorlds: !a.Has("no-child-worlds"),
            Seasons: a.Has("seasons"),
            Underside: a.Has("underside"),
            NativeGridObjects: a.Has("native-grid-objects"),
            NativeEffects: a.Has("native-effects"),
            PbrLod: a.Has("pbr-lod"),
            LargeReferences: a.Has("large-refs"),
            LargeRefsEsl: !a.Has("large-refs-no-esl"),
            PbrLodBrightness: (a.Get("pbr-lod-brightness") is { } pb && float.TryParse(pb, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pbp) ? Math.Clamp(pbp, 10f, 150f) : 100f) / 100f,
            PbrLodSize: PbrLodSizeFromArgs(a),
            UndersideStep: UndersideStepFromArgs(a),
            SkseDll: (a.Get("skse-dll") ?? "auto").ToLowerInvariant() switch
            {
                "auto" => SkseDllChoice.Auto,
                "1170" or "old" or "se" or "ae" => SkseDllChoice.UpTo1170,
                "17" or "new" or "1.7" => SkseDllChoice.Newer,
                "vr" or "1.4.15" => SkseDllChoice.Vr,
                "none" or "separate" => SkseDllChoice.None,
                var v => throw new CliArgException($"--skse-dll must be auto, 1170, 17, vr or none (got '{v}')."),
            },
            EslRange: a.Get("esl-range") is { } er && int.TryParse(er, out var erv) && erv is 2048 or 4096 ? erv : 0,
            DetailedShips: !a.Has("no-detailed-ships"),
            AnimatedShips: !a.Has("no-animated-ships"),
            CsGrassLod: !a.Has("no-cs-grass"),
            IceInset: a.Get("ice-inset") is { } ii && float.TryParse(ii, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var iip) ? Math.Clamp(iip, 0f, 64f) : AnvilLOD.Meshes.LodInset.DefaultIce,
            MountainFit: !a.Has("no-mountain-fit"),
            MountainInset: a.Get("mountain-inset") is { } mi && float.TryParse(mi, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var mip) ? Math.Clamp(mip, 0f, 64f) : 12f,
            Occlusion: a.Has("occlusion") ? new AnvilLOD.Core.Occlusion.OcclusionSettings(Clearance: a.Get("occlusion-clearance") is { } oc && float.TryParse(oc, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ocv) ? Math.Max(0f, ocv) : 3072f, ParentClearance: a.Get("occlusion-clearance") is { } oc2 && float.TryParse(oc2, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ocv2) ? Math.Max(0f, ocv2) : 3072f) : null,
            OcclusionFolder: a.Get("occlusion-folder"), ObjectAtlas: a.Has("object-atlas"),
            AtlasCellSize: a.Get("atlas-cell") is { } cell && int.TryParse(cell,out var size) ? size : a.Has("atlas-cell") ? throw new CliArgException("Invalid --atlas-cell.") : 256,
            MapObjectLevel: a.Get("map-objects")?.ToLowerInvariant() switch { null or "auto" => -2, "rules" => -1, "0" => 0, "1" => 1, "2" => 2, var bad => throw new CliArgException($"--map-objects must be auto, rules, 0, 1 or 2 (got '{bad}').") },
            SnowCover: a.Get("snow-cover") switch { "off" => 0, "bright" => 2, _ => 1 },
            SnowShaderMode: a.Get("snow-shader") switch { "plain" => 1, "hd" => 2, _ => 0 },
            ObjectInset: a.Get("object-inset") is { } oi && float.TryParse(oi, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var oip) ? Math.Clamp(oip, 0f, 64f) : 0f,
            ObjectBrightness: (a.Get("object-brightness") is { } ob && float.TryParse(ob, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var obp) ? Math.Clamp(obp, 10f, 110f) : 100f) / 100f,
            TreeBrightness: (a.Get("tree-brightness") is { } tb && float.TryParse(tb, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var tbp) ? tbp : 100f) / 100f,
            GrassDensity: (a.Get("grass-density") is { } gd && float.TryParse(gd, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pct) ? pct : 8f) / 100f,
            WaterStandIns: a.Has("water-standins"),
            GrassTop: (a.Get("grass-top") is { } gt && float.TryParse(gt, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var gtp) ? Math.Clamp(gtp, 20f, 120f) : 85f) / 100f,
            GrassBottom: (a.Get("grass-bottom") is { } gb && float.TryParse(gb, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var gbp) ? Math.Clamp(gbp, 10f, 120f) : 50f) / 100f);

        // Progress<T> posts asynchronously; a synchronous reporter keeps console output ordered.
        var sync = new SyncProgress(m => Console.WriteLine($"[{total.Elapsed:mm\\:ss\\.f}] {m}"));
        if(a.Get("settings") is { } scanPreset) req=RequestFactory.Scan(SettingsStore.Read(scanPreset),generate);
        ScanSummary s;
        if(generate)
        {
            var generated=GenerationPipeline.Run(new(Lod:req,Resume:!a.Has("no-resume"),VerifySourceContents:a.Has("verify-source-contents"),Preflight:!a.Has("no-preflight"),TextureCompute:a.Get("texture-compute")??"Cpu"),
                CacheFolder(a),sync,Cancellation);
            Console.WriteLine("Saved job: "+generated.JobFolder);
            if(!generated.Success)return Fail(generated.Error!);
            if(generated.Lod is null){Console.WriteLine("Reused verified LOD output.");return 0;}
            s=generated.Lod;
        }
        else s=ScanPipeline.Run(req,sync,Cancellation);

        total.Stop();

        Console.WriteLine();
        Console.WriteLine("=== Scan summary ===");
        Console.WriteLine($"Plugins:              {s.PluginsLoaded:N0} found of {s.Stats.PluginsInLoadOrder:N0} listed");
        Console.WriteLine($"Worldspaces w/ .lod:  {s.Grids.Count} ({string.Join(", ", s.Grids.Keys.Take(8))}{(s.Grids.Count > 8 ? ", ..." : "")})");
        Console.WriteLine($"REFRs visited:        {s.Stats.PlacedObjectsVisited:N0}");
        Console.WriteLine($"LOD references:       {s.Stats.LodReferencesFound:N0}");
        Console.WriteLine($"Skipped (disabled):   {s.Stats.SkippedDisabled:N0}");
        Console.WriteLine($"Skipped (no mesh):    {s.Stats.SkippedMissingMesh:N0}");
        Console.WriteLine($"Outside LOD grid:     {s.SkippedOutsideGrid:N0}");
        Console.WriteLine($"Missing LOD meshes:   {s.Stats.MissingMeshes.Count:N0} unique paths");
        Console.WriteLine($"Dynamic LOD refs:     {s.DynamicRefs:N0}");
        Console.WriteLine($"Grass cache cells:    {s.GrassCells:N0}" + (s.MissingGrassBillboards is { Count: > 0 } mg ? $" ({mg.Count} grass types without billboard)" : ""));
        Console.WriteLine($"Trees with billboard: {s.TreeReferences:N0} ({s.MissingBillboards?.Count ?? 0:N0} tree types without one)");
        foreach (var level in LodLevels.All)
            Console.WriteLine($"  LOD{(int)level,-2} blocks:        {s.Quads.Keys.Count(q => q.Level == level):N0}");
        Console.WriteLine($"Build plan:           {s.Plan.Rebuild.Count:N0} rebuild, {s.Plan.Unchanged.Count:N0} unchanged, {s.Plan.StaleFiles.Count:N0} stale");
        Console.WriteLine();
        Console.WriteLine($"Timing: index {s.IndexTime.TotalSeconds:F1}s | scan {s.Stats.Elapsed.TotalSeconds:F1}s | bucket+hash {s.BucketAndHashTime.TotalSeconds:F2}s | total {total.Elapsed.TotalSeconds:F1}s");

        if (s.Generation is { } g)
        {
            Console.WriteLine($"Generated:            {g.BlocksWritten:N0} blocks, {g.Triangles:N0} triangles ({g.TrianglesCulled:N0} buried removed), {g.MeshesLoaded:N0} meshes, {g.Elapsed.TotalSeconds:F1}s");
            if (s.StaleFilesDeleted > 0) Console.WriteLine($"Removed stale:        {s.StaleFilesDeleted:N0} blocks");
            if (s.Trees is { } t)
                Console.WriteLine($"Tree LOD:             {t.Instances:N0} trees, {t.TreeTypes} types, {t.Blocks:N0} blocks, {t.EmptyBlocks:N0} vanilla blocks emptied, {t.Elapsed.TotalSeconds:F1}s");
            foreach (var (mesh, err) in g.MeshErrors.Take(20)) Console.WriteLine($"  mesh problem: {mesh}: {err}");
        }
        foreach (var w in s.Warnings) Console.WriteLine($"WARNING: {w}");

        var reportPath = a.Get("report") ?? Path.Combine(AppContext.BaseDirectory, "AnvilLOD.scan.json");
        ScanReport.Write(reportPath, s, total.Elapsed);
        Console.WriteLine($"Report: {reportPath}");
        return s.Generation is { BlocksFailed: > 0 } || s.Grids.Count == 0 ? 1 : 0;
    }

    private static int RunAuthor(CliArgs a)
    {
        var plugin = a.Get("plugin") ?? throw new CliArgException("author needs --plugin <file name>, e.g. --plugin \"The Archwood.esp\".");
        var output = a.Get("author-out") ?? throw new CliArgException("author needs --author-out <folder> (a new, separate mod folder).");
        float F(string key, float def) => a.Get(key) is { } v && float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : def;
        var total = Stopwatch.StartNew();
        var req = new AnvilLOD.Plugins.Authoring.AuthorRequest(
            a.Get("mo2") is { } mo2
                ? new GameContextOptions(Mode: GameSourceMode.Mo2Instance, Mo2InstanceFolder: mo2, Mo2Profile: a.Get("profile"), Mo2Locations: Mo2LocationsFromArgs(a), GameIniPath: a.Get("game-ini"), WinePrefix: a.Get("wine-prefix"))
                : new GameContextOptions(a.Get("data"), a.Get("plugins"), GameIniPath: a.Get("game-ini"), WinePrefix: a.Get("wine-prefix")),
            plugin, output,
            LodMeshes: !a.Has("no-meshes"),
            Billboards: !a.Has("no-billboards"),
            TreeStandIns: a.Has("tree-stand-ins"),
            RuleFile: !a.Has("no-rules"),
            MinObjectSize: F("min-size", 400f),
            MinTreeHeight: F("min-tree-height", 256f),
            Overwrite: a.Has("overwrite"),
            DynDolodFolder: a.Get("dyndolod"),
            Preset: ParsePreset(a.Get("preset")),
            Budget: ParseBudget(a.Get("budget")).Mode,
            CustomBudget: ParseBudget(a.Get("budget")).Custom);
        Console.WriteLine("WARNING: " + AnvilLOD.Plugins.Authoring.ModAuthorTool.Warning);
        var sync = new SyncProgress(m => Console.WriteLine($"[{total.Elapsed:mm\\:ss\\.f}] {m}"));
        var r = AnvilLOD.Plugins.Authoring.ModAuthorTool.Run(req, sync, Cancellation);
        Console.WriteLine();
        foreach (var g in r.Items.GroupBy(i => (i.Kind, Status: i.Status.Split(':')[0])).OrderBy(g => g.Key.Kind).ThenBy(g => g.Key.Status))
            Console.WriteLine($"  {g.Key.Kind,-7} {g.Key.Status,-45} {g.Count(),5}");
        Console.WriteLine($"Generated {r.MeshesWritten} LOD meshes, {r.BillboardsWritten} billboards{(r.RuleFile is null ? "" : ", rule file " + r.RuleFile)}");
        Console.WriteLine($"Report: {r.ReportFile}");
        return 0;
    }

    private static (AnvilLOD.Meshes.Authoring.BudgetMode Mode, IReadOnlyList<int>? Custom) ParseBudget(string? v)
    {
        if (v is null || v.Equals("recommended", StringComparison.OrdinalIgnoreCase)) return (AnvilLOD.Meshes.Authoring.BudgetMode.Recommended, null);
        if (v.Equals("off", StringComparison.OrdinalIgnoreCase)) return (AnvilLOD.Meshes.Authoring.BudgetMode.Off, null);
        var nums = v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => int.TryParse(x, out var n) && n >= 0 ? n : -1).ToList();
        if (nums.Count is < 1 or > 3 || nums.Any(n => n < 0))
            throw new CliArgException($"--budget must be recommended, off, or up to three numbers LOD0,LOD1,LOD2 (got '{v}').");
        while (nums.Count < 3) nums.Add(0);
        return (AnvilLOD.Meshes.Authoring.BudgetMode.Custom, nums);
    }

    private static int RunFixTextures(CliArgs a)
    {
        var folders = new List<string>(a.GetAll("input") ?? []);
        string? mo2Root = null;
        if (a.Get("mo2") is { } mo2)
        {
            var instance = AnvilLOD.Plugins.Mo2.Mo2Instance.Open(mo2, Mo2LocationsFromArgs(a));
            folders.AddRange(instance.OpenProfile(a.Get("profile")).EnabledModFolders());
            mo2Root = instance.InstanceFolder;
        }
        if (folders.Count == 0) throw new CliArgException("fix-textures needs --input <folder> (repeatable) or --mo2 <instance>.");
        var total = Stopwatch.StartNew();
        var sync = new SyncProgress(m => Console.WriteLine($"[{total.Elapsed:mm\\:ss\\.f}] {m}"));
        var bad = AnvilLOD.Core.Textures.TextureFixer.Scan(folders, sync, Cancellation);
        foreach (var b in bad.OrderBy(b => b.Mod, StringComparer.OrdinalIgnoreCase).ThenBy(b => b.Relative, StringComparer.OrdinalIgnoreCase))
            Console.WriteLine($"  {b.Mod}\\{b.Relative}  {b.Format}  {b.Width}x{b.Height}, {b.Mips} mips  {(b.Fixable ? "can be converted" : "cannot be converted here")}");
        if (!a.Has("apply") || bad.Count == 0)
        {
            if (bad.Count > 0) Console.WriteLine("Nothing was changed. Add --apply (with --output <folder> or --in-place --backup <folder>) to convert them.");
            return 0;
        }
        bool inPlace = a.Has("in-place");
        var output = a.Get("output");
        var backup = a.Get("backup");
        if (inPlace == (output is not null)) throw new CliArgException("--apply needs either --output <folder> or --in-place with --backup <folder>.");
        if (inPlace && backup is null) throw new CliArgException("--in-place needs --backup <folder> (the originals are copied there first).");
        var texconv = a.Get("texconv");
        if (texconv is not null && !File.Exists(texconv)) throw new CliArgException("--texconv must name an executable file.");
        if (texconv is not null && !OperatingSystem.IsWindows() && texconv.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new CliArgException("Use a Linux texconv executable, or omit --texconv for native managed BC6H conversion.");
        Console.WriteLine(texconv is null ? "Using native managed BC6H → BC7 conversion" : $"Using {texconv}");
        var results = AnvilLOD.Core.Textures.TextureFixer.Fix(bad, texconv ?? "", inPlace ? null : output, inPlace ? backup : null,
            dryRun: false, sync, Cancellation, nativeConverter: texconv is null ? AnvilLOD.Textures.NativeTextureConverter.Bc6ToBc7 : null);
        foreach (var r in results.Where(r => !r.Status.StartsWith("Converted", StringComparison.Ordinal))) Console.WriteLine($"  {r.Source.Mod}\\{r.Source.Relative}: {r.Status}");
        return results.Any(r => r.Status.StartsWith("Failed", StringComparison.Ordinal)) ? 1 : 0;
    }

    private static int RunConvertLod(CliArgs a)
    {
        var source = a.Get("source") ?? throw new CliArgException("convert-lod needs --source <mod folder>.");
        var output = a.Get("output") ?? throw new CliArgException("convert-lod needs --output <folder> (a new, separate mod folder).");
        var total = Stopwatch.StartNew();
        Console.WriteLine("WARNING: " + AnvilLOD.Plugins.Authoring.OldrimLodConverter.Warning);
        var sync = new SyncProgress(m => Console.WriteLine($"[{total.Elapsed:mm\\:ss\\.f}] {m}"));
        var r = AnvilLOD.Plugins.Authoring.OldrimLodConverter.Run(new AnvilLOD.Plugins.Authoring.ConvertRequest(source, output, a.Has("all-meshes"), a.Has("overwrite")), sync, Cancellation);
        Console.WriteLine($"Report: {r.ReportFile}");
        return 0;
    }

    private static int RunLodMaker(CliArgs a)
    {
        var inputs = a.GetAll("input") ?? throw new CliArgException("lodmaker needs at least one --input <file or folder>.");
        string output;
        if (a.Get("new-mod") is { } modName)
        {
            var mo2 = a.Get("mo2") ?? throw new CliArgException("--new-mod needs --mo2 <instance folder>, to find the mods folder.");
            if (a.Get("output") is not null) throw new CliArgException("Use either --output or --new-mod, not both.");
            output = Path.Combine(AnvilLOD.Plugins.Mo2.Mo2Instance.Open(mo2, Mo2LocationsFromArgs(a)).ModsFolder, modName.Trim());
        }
        else output = a.Get("output") ?? throw new CliArgException("lodmaker needs --output <folder> (or --mo2 <instance> --new-mod <name>).");

        float F(string key, float def) => a.Get(key) is { } v && float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : def;
        IReadOnlyList<int>? levels = null;
        if (a.Get("levels") is { } lv)
        {
            var parsed = lv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => int.TryParse(x, out var n) ? n : -1).ToList();
            if (parsed.Count == 0 || parsed.Any(n => n is < 0 or > 2)) throw new CliArgException($"--levels must be a list of 0, 1 and 2 (got '{lv}').");
            levels = parsed;
        }
        var detail = F("detail", 1f);
        if (!(detail >= 0.1f && detail <= 10f)) throw new CliArgException("--detail must be between 0.1 and 10.");
        var group = a.Get("group") ?? Path.GetFileName(Path.TrimEndingDirectorySeparator(output));
        var (budget, custom) = ParseBudget(a.Get("budget"));

        var total = Stopwatch.StartNew();
        var req = new AnvilLOD.Plugins.Authoring.LodMakerRequest(inputs.ToList(), output, group, levels, detail, F("min-size", 0f),
            RuleFile: !a.Has("no-rules"), Overwrite: a.Has("overwrite"), Budget: budget, CustomBudget: custom, Plugin: a.Get("plugin"));
        Console.WriteLine("WARNING: " + AnvilLOD.Plugins.Authoring.LodMeshMaker.Warning);
        var sync = new SyncProgress(m => Console.WriteLine($"[{total.Elapsed:mm\\:ss\\.f}] {m}"));
        var r = AnvilLOD.Plugins.Authoring.LodMeshMaker.Run(req, sync, Cancellation);
        Console.WriteLine();
        foreach (var g in r.Items.GroupBy(i => i.Status.Split(':')[0]).OrderBy(g => g.Key))
            Console.WriteLine($"  {g.Key,-48} {g.Count(),5}");
        Console.WriteLine($"{r.ModelsDone} models, {r.MeshesWritten} LOD meshes{(r.RuleFile is null ? "" : ", rule file " + r.RuleFile)}");
        Console.WriteLine($"Output: {output}");
        Console.WriteLine($"Report: {r.ReportFile}");
        return 0;
    }

    private static AnvilLOD.Core.Lod.LodPreset ParsePreset(string? s) => s?.ToLowerInvariant() switch
    {
        null or "high" => AnvilLOD.Core.Lod.LodPreset.High,
        "medium" => AnvilLOD.Core.Lod.LodPreset.Medium,
        "low" => AnvilLOD.Core.Lod.LodPreset.Low,
        _ => throw new CliArgException($"Unknown preset '{s}' (use low, medium or high)."),
    };

    private static int Fail(string msg)
    {
        Console.Error.WriteLine(msg);
        return 2;
    }

    private sealed class SyncProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }
}
