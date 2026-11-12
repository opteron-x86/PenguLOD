using PenguLOD.Core.Lod;
using PenguLOD.Core.Occlusion;
using PenguLOD.Core.Paths;
using PenguLOD.Core.Pipeline;
using PenguLOD.Core.World;
using PenguLOD.Desktop.Models;
using PenguLOD.Plugins;
using PenguLOD.Plugins.Mo2;

namespace PenguLOD.Desktop.Services;

public static class RequestFactory
{
    public static string? Optional(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    public static string PathValue(string value, DesktopSettings s) => HostPath.Resolve(value, relativeTo: s.BaseFolder, winePrefix: Optional(s.WinePrefix));
    public static string RequiredPath(string value, string label, DesktopSettings s)
        => Optional(value) is { } p ? PathValue(p, s) : throw new ArgumentException($"Choose {label} first.");
    public static string[] Lines(string text) => text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string[]? Worldspaces(string text)
    {
        var worlds = text.Split([',', ';', ' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return worlds.Length == 0 || worlds.Contains("*") ? null : worlds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static string[] ResourcePaths(DesktopSettings s) => Lines(s.ResourceFolders).Select(p => PathValue(p, s)).ToArray();

    public static GameContextOptions Game(DesktopSettings s)
    {
        string? P(string value) => Optional(value) is { } p ? PathValue(p, s) : null;
        bool mo2 = s.SourceMode == "Mod Organizer 2";
        if (mo2 && Optional(s.Instance) is null) throw new ArgumentException("Choose the Mod Organizer 2 instance folder.");
        if (!mo2 && Optional(s.GameFolder) is null) throw new ArgumentException("Choose the game or Data folder.");
        if (!mo2 && Optional(s.PluginsFile) is null) throw new ArgumentException("Choose plugins.txt. An empty file is valid for a vanilla game.");
        return new GameContextOptions(Release: s.Edition == "VR" ? Mutagen.Bethesda.GameRelease.SkyrimVR : Mutagen.Bethesda.GameRelease.SkyrimSE,
            DataFolder: mo2 ? null : P(s.GameFolder), PluginsTxt: mo2 ? null : P(s.PluginsFile),
            Mode: mo2 ? GameSourceMode.Mo2Instance : GameSourceMode.DataFolder,
            Mo2InstanceFolder: P(s.Instance), Mo2Profile: Optional(s.Profile),
            Mo2Locations: new Mo2Locations(P(s.Mo2GameFolder), P(s.ModsFolder), P(s.ProfilesFolder), P(s.OverwriteFolder), P(s.WinePrefix)),
            GameIniPath: P(s.GameIni), WinePrefix: P(s.WinePrefix));
    }

    public static GenerationPipelineRequest Pipeline(DesktopSettings s) =>
        Stages(s, s.PipelineGrass, s.PipelineTextures, s.PipelineTerrain, s.PipelineLod);

    /// <summary>
    /// A pipeline request for the chosen stages. Every generation button uses this, so run settings (resume, source
    /// verification, setup check, texture compute, scope expansion) apply the same way to single stages and the full pipeline.
    /// </summary>
    public static GenerationPipelineRequest Stages(DesktopSettings s, bool grass = false, bool textures = false, bool terrain = false, bool lod = false) => new(
        grass ? Grass(s) : null, textures ? Textures(s) : null,
        lod ? Scan(s, true) : null, s.ResumeCompletedStages, s.VerifySourceContents,
        terrain ? Terrain(s) : null, s.PipelinePreflight, s.TextureCompute, s.ExpandOutputScope, KeepPrevious(s));

    public static int KeepPrevious(DesktopSettings s) =>
        s.KeepPreviousOutputs == decimal.Truncate(s.KeepPreviousOutputs) && s.KeepPreviousOutputs is >= 0 and <= PenguLOD.Core.Pipeline.OutputRetention.Maximum
            ? (int)s.KeepPreviousOutputs
            : throw new ArgumentException($"Previous outputs to keep must be a whole number from 0 to {PenguLOD.Core.Pipeline.OutputRetention.Maximum}.");

    public static IReadOnlyList<GenerationScopeStage> OutputScope(DesktopSettings s)
    {
        var stages = new List<GenerationScopeStage>();
        if (s.PipelineGrass) stages.Add(new("grass", RequiredPath(s.GrassOutput, "a grass cache output folder", s), Worldspaces(s.GrassWorldspaces)));
        if (s.PipelineTextures) stages.Add(new("textures", RequiredPath(s.TextureGenOutput, "a texture output folder", s)));
        if (s.PipelineTerrain) stages.Add(new("terrain", RequiredPath(s.TerrainOutput, "a terrain output folder", s), Worldspaces(s.Worldspaces)));
        if (s.PipelineLod)
        {
            var levels = LodLevels.All.Where(l => GenerationOptions.Get<bool>(s, "lod" + (int)l)).ToArray();
            if (levels.Length == 0) throw new ArgumentException("Select at least one LOD level on Object LOD.");
            stages.Add(new("lod", RequiredPath(s.OutputFolder, "an object LOD output folder", s), Worldspaces(s.Worldspaces), levels));
        }
        return stages;
    }

    public static TerrainGenerationRequest Terrain(DesktopSettings s)
    {
        int Whole(decimal value) => value == decimal.Truncate(value) ? checked((int)value) : throw new ArgumentException("Terrain sizes must be whole numbers.");
        var request = new TerrainGenerationRequest(Game(s), RequiredPath(s.TerrainOutput, "a terrain output folder", s), Worldspaces(s.Worldspaces),
            TextureSize: Whole(s.TerrainTextureSize), Brightness: (float)s.TerrainBrightness, VertexColorStrength: (float)s.TerrainVertexColor,
            TextureRepeatsPerCell: (float)s.TerrainRepeats, SkirtDepth: (float)s.TerrainSkirt, Water: s.TerrainWater,
            UseExistingTerrain: s.TerrainFallback, ExportHeightmaps: s.TerrainHeightmaps, HeightmapMaximumSize: Whole(s.TerrainHeightmapSize), Seasons: s.Seasons,
            Materials: new((float)s.TerrainNormalStrength, s.TerrainPbr, s.TerrainPbrTwins, (float)s.TerrainPbrScale, (float)s.TerrainPbrGamma, (float)s.TerrainAo));
        TerrainGeneration.Validate(request);
        return request;
    }

    public static TextureGenerationRequest Textures(DesktopSettings s)
    {
        if (s.TextureGenBaseSize != decimal.Truncate(s.TextureGenBaseSize) || s.TextureGenMaxSize != decimal.Truncate(s.TextureGenMaxSize))
            throw new ArgumentException("Texture sizes must be whole numbers.");
        var request = new TextureGenerationRequest(Game(s), RequiredPath(s.TextureGenOutput, "a texture output folder", s),
            Optional(s.DynDolodFolder) is { } d ? PathValue(d, s) : null,
            s.TextureGenStitched, s.TextureGenTrees, s.TextureGenGrass, s.TextureGenObjects,
            (int)s.TextureGenBaseSize, (int)s.TextureGenMaxSize, (float)s.TextureGenUnitsPerPixel, (float)s.TextureGenBrightness,
            (float)s.TextureGenMinTreeHeight, (float)s.TextureGenMinGrassHeight, s.TextureGenFilter.Trim(), TreeTrunks: s.TextureGenTrunks, Materials: new PenguLOD.Plugins.Authoring.MaterialBakeSettings(s.BakeSourceNormals, s.BakePbr,
                (float)s.BakePbrScale, (float)s.BakePbrGamma, (float)s.BakeAo, (float)s.BakeAmbient, (float)s.BakeDirectional),
            Billboards: new(s.TextureGenHdViews, s.TextureGenReferenceScale, (float)s.TextureGenUnplacedTreeScale,
                Enum.TryParse<PenguLOD.Plugins.Authoring.GrassTextureMode>(s.TextureGenGrassTextures, out var grassTextures) ? grassTextures
                    : throw new ArgumentException("Grass texture layout must be Auto, Basic or Packed."), (float)s.TextureGenComplexThreshold), ResourcePacks: ResourcePaths(s));
        TextureGeneration.Validate(request);
        if (Optional(s.GrassOutput) is { } grassOutput)
        {
            var grass = PathValue(grassOutput, s);
            if (HostPath.IsWithin(grass, request.OutputFolder) || HostPath.IsWithin(request.OutputFolder, grass))
                throw new ArgumentException("Choose separate texture and grass output folders; neither may contain the other.");
        }
        if (Optional(s.OutputFolder) is { } folder)
        {
            var lod = PathValue(folder, s);
            if (HostPath.IsWithin(lod, request.OutputFolder) || HostPath.IsWithin(request.OutputFolder, lod))
                throw new ArgumentException("Choose separate texture and LOD output folders; neither may contain the other.");
        }
        return request;
    }

    public static GrassGenerationRequest Grass(DesktopSettings s)
    {
        int Whole(decimal value)
        {
            if (value != decimal.Truncate(value)) throw new ArgumentException("Grass cache counts and cell coordinates must be whole numbers.");
            return checked((int)value);
        }
        var request = new GrassGenerationRequest(Game(s), RequiredPath(s.GrassOutput, "a grass cache output folder", s),
            Optional(s.GrassExecutable) is { } exe ? PathValue(exe, s) : null,
            s.GrassWorldspaces.Trim(), s.GrassPlacement.ToLowerInvariant(), s.GrassRejection.ToLowerInvariant(), s.GrassSeasons.ToLowerInvariant(),
            Optional(s.GrassNgioConfig) is { } config ? PathValue(config, s) : null,
            s.GrassPreview, Whole(s.GrassCellX), Whole(s.GrassCellY), Whole(s.GrassRadius), Whole(s.MaxThreads), Whole(s.GrassWriters),
            s.GrassRenderGeometry, s.GrassMinSize == 0 ? null : Whole(s.GrassMinSize));
        GrassGeneration.Validate(request);
        foreach (var other in new[] { s.OutputFolder, s.TextureGenOutput }.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            var path = PathValue(other, s);
            if (HostPath.IsWithin(path, request.OutputFolder) || HostPath.IsWithin(request.OutputFolder, path))
                throw new ArgumentException("Choose separate grass, texture and LOD output folders; none may contain another.");
        }
        return request;
    }

    public static ScanRequest Scan(DesktopSettings s, bool generate)
    {
        GenerationOptions.Validate(s);
        bool B(string k) => GenerationOptions.Get<bool>(s, k);
        float F(string k) => (float)GenerationOptions.Get<decimal>(s, k);
        int I(string k)
        {
            if (!GenerationOptions.WholeNumbers.Contains(k)) throw new InvalidOperationException($"Option {k} is not declared as a whole number.");
            decimal value = GenerationOptions.Get<decimal>(s, k);
            if (value != decimal.Truncate(value)) throw new ArgumentException($"{GenerationOptions.All.Single(o => o.Key == k).Label} must be a whole number.");
            return (int)value;
        }
        string C(string k) => GenerationOptions.Get<string>(s, k);
        var levels = LodLevels.All.Where(l => B("lod" + (int)l)).ToArray();
        if (levels.Length == 0) throw new ArgumentException("Select at least one LOD level in Generation settings.");
        string? output = Optional(s.OutputFolder) is { } folder ? PathValue(folder, s) : null;
        if (generate && output is null) throw new ArgumentException("Choose an output mod folder before generating.");
        var game = Game(s);
        if (generate) GenerationDestination.Validate(game, output!);
        return new ScanRequest(game, new ScanOptions(Worldspaces: Worldspaces(s.Worldspaces),
            IncludeInitiallyDisabled: B("disabled"), IncludeEnableParented: B("parented"), TreeLod: B("trees"),
            DynamicLod: B("dynamic"), GridObjects: B("grid"), Tree3D: new Tree3DSettings(B("tree3d"), B("tree3d8"), B("treePlain"), B("mapTrees"), B("treeNamed"), I("treeNearBudget"), I("treeFarBudget"), F("treeNearError"), F("treeFarError"), B("treeNative"), B("treeFoliage"), F("treeNearCoverage") / 100, F("treeFarCoverage") / 100, B("treeHybrid"), int.Parse(C("treeTrunkSize")), F("treeTrunkUnits"), F("treeFoliageTarget") / 100),
            MatchTextures: B("matchTextures"), ParentCopies: B("parentCopies"), GlowWindows: B("glowWindows"), PbrWaterfalls: B("pbrWaterfalls"), RenderTreeBillboards: B("renderBillboards")),
            OutputFolder: output, Levels: levels, Generate: generate,
            DynDolodFolder: Optional(s.DynDolodFolder) is { } d ? PathValue(d, s) : null, ResourcePacks: ResourcePaths(s),
            CustomRulesFile: Optional(s.CustomRulesFile) is { } r ? PathValue(r, s) : null,
            Preset: Enum.Parse<LodPreset>(C("preset")), RemoveBuried: B("removeBuried"), RemoveUnderwater: B("removeUnderwater"),
            GrassLod: B("grass"), GrassDensity: F("grassDensity"), GrassSize: F("grassSize"), GrassTop: F("grassTop"), GrassBottom: F("grassBottom"),
            TreeBrightness: F("treeBrightness"), ObjectBrightness: F("objectBrightness"), WaterStandIns: B("water"), Seasons: B("seasons"), ChildWorlds: B("childWorlds"),
            SkseDll: C("skse") switch { "Auto" => SkseDllChoice.Auto, "1.5.97–1.6.1170" => SkseDllChoice.UpTo1170, "1.7.x" => SkseDllChoice.Newer, "VR" => SkseDllChoice.Vr, _ => SkseDllChoice.None },
            Underside: B("underside"), UndersideStep: int.Parse(C("undersideStep")), EslRange: C("esl") == "Auto" ? 0 : int.Parse(C("esl")),
            NativeEffects: B("nativeEffects"), NativeGridObjects: B("nativeGrid"), PbrLod: B("pbr"), PbrLodBrightness: F("pbrBrightness"), PbrLodSize: int.Parse(C("pbrSize")),
            LargeReferences: B("largeRefs"), LargeRefsEsl: B("largeEsl"), Candles: B("candles"), FxGlow: B("fxGlow"), CleanOutput: s.CleanOutput,
            IceInset: F("iceInset"), MountainFit: B("mountainFit"), MountainInset: F("mountainInset"), ObjectInset: F("objectInset"),
            SnowShaderMode: I("snowMode"), SnowCover: I("snowCover"), OpaqueAlpha: B("opaqueAlpha"), DetailedShips: B("detailedShips"), AnimatedShips: B("animatedShips"),
            CsGrassLod: B("csGrass"), MapObjectLevel: I("mapLevel"),
            Occlusion: generate && B("occlusion") ? new OcclusionSettings(I("occRadius"), F("occClearance"), I("occSamples"), F("occEye"), F("occParent"), I("occParentTargets"), I("occBlockTargets"), I("occEyes")) : null,
            GeneratedTextureFolder: s.UseGeneratedTextures ? RequiredPath(s.TextureGenOutput, "a texture output folder", s) : null,
            GeneratedGrassFolder: s.UseGeneratedGrass ? RequiredPath(s.GrassOutput, "a grass cache output folder", s) : null,
            ObjectAtlas: B("objectAtlas"), AtlasCellSize: int.Parse(C("atlasCell")),
            OptimizeObjectTextures: B("objectTextures"), ObjectTextureSize: int.Parse(C("objectTextureSize")),
            AutomaticObjects: new PenguLOD.Plugins.Authoring.AutomaticObjectSettings(B("autoObjects"), I("autoObjectNear"), I("autoObjectMiddle"), I("autoObjectFar"), F("autoObjectMin"), F("autoObjectDetail")),
            GeneratedTerrainFolder: s.UseGeneratedTerrain ? RequiredPath(s.TerrainOutput, "a terrain output folder", s) : null);
    }
}
