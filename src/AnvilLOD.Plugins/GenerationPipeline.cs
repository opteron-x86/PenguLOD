using System.Diagnostics;
using System.Text.Json;
using AnvilLOD.Core.Paths;
using AnvilLOD.Core.World;
using AnvilLOD.Core.Pipeline;

namespace AnvilLOD.Plugins;

public sealed record GenerationPipelineRequest(GrassGenerationRequest? Grass = null, TextureGenerationRequest? Textures = null,
    ScanRequest? Lod = null, bool Resume = true, bool VerifySourceContents = false, TerrainGenerationRequest? Terrain = null, bool Preflight = true, string TextureCompute = "Cpu", bool ExpandOutputScope = false);
public sealed record LodStageReceipt(string OutputFolder, int Files, int BlocksWritten, int BlocksUnchanged,
    long Triangles, IReadOnlyList<string> Warnings, string? PreviousOutput);
public sealed record GenerationPipelineResult(string JobFolder, PipelineInputSnapshot Inputs, PipelineJournal Journal,
    GrassGenerationResult? Grass, TextureGenerationResult? Textures, LodStageReceipt? LodReceipt, ScanSummary? Lod,
    string? FailedStage, string? Error, TerrainGenerationResult? Terrain = null, DiagnosticReport? Diagnostics = null, IReadOnlyList<SeasonalOutputCoverage>? Seasons = null)
{
    public bool Success => Error is null;
}

/// <summary>Shared persistent orchestration for desktop and CLI. Generated inputs are explicit dependencies.</summary>
public static class GenerationPipeline
{
    public static GenerationPipelineResult Run(GenerationPipelineRequest request, string cache,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var scope = GenerationScope.Review(request, ct);
        if (scope.RequiresExpansion)
        {
            if (!request.ExpandOutputScope) throw new InvalidDataException(scope.Summary());
            progress?.Report(scope.Summary()); request = scope.Request;
        }
        using var compute = AnvilLOD.Textures.TextureCompute.Enter(request.TextureCompute, progress);
        var source = request.Grass?.Game ?? request.Textures?.Game ?? request.Lod?.Game ?? request.Terrain?.Game
            ?? throw new ArgumentException("Select at least one pipeline stage.");
        GenerationDestination.ValidateCache(source,cache);
        var outputs = new[] { request.Grass?.OutputFolder, request.Textures?.OutputFolder, request.Lod?.OutputFolder, request.Terrain?.OutputFolder }.OfType<string>().ToArray();
        foreach (var output in outputs)
        {
            GenerationDestination.Validate(source, output);
            if (OutputSetReceipt.Exists(output))
            {
                var previous = OutputSetReceipt.Read(output);
                if (previous.Stages.Count != 1) throw new InvalidDataException("Generate into separate stage folders, not a combined installation package.");
                string expectedStage = output == request.Grass?.OutputFolder ? "grass" : output == request.Textures?.OutputFolder ? "textures" : output == request.Terrain?.OutputFolder ? "terrain" : "lod";
                if (previous.Stages[0].Id != expectedStage) throw new InvalidDataException("This folder belongs to a different generation stage. Choose a separate output folder.");
                var owned = previous.Stages[0].Files.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var unknown = OutputFiles.Enumerate(output, ct).FirstOrDefault(p => !owned.Contains(p)
                    && !p.Equals(OutputSetReceipt.FileName, StringComparison.OrdinalIgnoreCase) && !p.Equals("meta.ini", StringComparison.OrdinalIgnoreCase));
                if (unknown is not null) throw new InvalidDataException("Existing output contains an unowned file: " + unknown + ". Choose a separate generation folder.");
            }
            ResourceCatalog.ValidateFolders(request.Textures?.DynDolodFolder, request.Textures?.ResourcePacks, output);
            ResourceCatalog.ValidateFolders(request.Lod?.DynDolodFolder, request.Lod?.ResourcePacks, output);
            if (outputs.Count(other => HostPath.IsWithin(output, other) || HostPath.IsWithin(other, output)) != 1)
                throw new ArgumentException("Pipeline stages need separate output folders; none may contain another.");
            if (HostPath.IsWithin(cache, output) || HostPath.IsWithin(output, cache))
                throw new ArgumentException("Keep the pipeline cache and generated output folders separate.");
        }
        foreach (var game in new[] { request.Grass?.Game, request.Textures?.Game, request.Lod?.Game, request.Terrain?.Game }.OfType<GameContextOptions>())
            if (JsonSerializer.Serialize(game) != JsonSerializer.Serialize(source))
                throw new ArgumentException("Every pipeline stage must use the same game source and profile.");
        if (request.Lod is not null && (request.Grass?.Preview == true || request.Textures?.ModelFilter.Length > 0))
            throw new ArgumentException("Clear grass previews and texture model filters before a combined LOD job.");
        source = source with { ExcludeFolders = (source.ExcludeFolders ?? []).Concat(outputs).Distinct().ToArray() };
        var grass = request.Grass is { } g ? g with { Game = source } : null;
        var textures = request.Textures is { } t ? t with { Game = source } : null;
        var terrain = request.Terrain is { } tr ? tr with { Game = source } : null;
        var lod = request.Lod is { } l ? l with { Game = source, Generate = true,
            GeneratedGrassFolder = grass?.OutputFolder ?? l.GeneratedGrassFolder,
            GeneratedTextureFolder = textures?.OutputFolder ?? l.GeneratedTextureFolder,
            GeneratedTerrainFolder = terrain?.OutputFolder ?? l.GeneratedTerrainFolder } : null;
        if (grass is not null) GrassGeneration.Validate(grass);
        if (textures is not null) TextureGeneration.Validate(textures);
        if (lod is { OutputFolder: null }) throw new ArgumentException("Choose a LOD output folder.");
        if (terrain is not null) TerrainGeneration.Validate(terrain);
        var plan = new List<PipelineStagePlan>();
        if (grass is not null) plan.Add(new("grass", "Grass cache", grass.OutputFolder, []));
        if (textures is not null) plan.Add(new("textures", "Textures", textures.OutputFolder, []));
        if (terrain is not null) plan.Add(new("terrain", "Terrain and water", terrain.OutputFolder, []));
        if (lod is not null) plan.Add(new("lod", "LOD", lod.OutputFolder!, plan.Select(p => p.Id).ToArray()));
        using var job = new PersistentPipeline(cache, plan, request.Resume, progress, ct);
        var inputs = PipelineInputs.Capture(source, outputs, [], request.VerifySourceContents, progress, ct);
        File.WriteAllText(Path.Combine(job.Folder, "inputs.json"), JsonSerializer.Serialize(inputs, PersistentPipeline.Json));
        File.WriteAllText(Path.Combine(job.Folder, "request.json"), JsonSerializer.Serialize(request, PersistentPipeline.Json));
        GrassGenerationResult? grassResult = null; TextureGenerationResult? textureResult = null;
        TerrainGenerationResult? terrainResult = null; DiagnosticReport? diagnostics = null;
        ScanSummary? summary = null; LodStageReceipt? receipt = null;
        string? active = null, error = null; IReadOnlyList<SeasonalOutputCoverage>? seasonalOutput = null;
        string Fingerprint<T>(T value, params string?[] extra) => inputs.Fingerprint + (value is GrassGenerationRequest ? "" : "|compute:" + request.TextureCompute) + "|" + JsonSerializer.Serialize(value)
            + "|" + PipelineInputs.ExternalFingerprint(extra, request.VerifySourceContents, ct);
        try
        {
            if (request.Preflight)
            {
                active = "preflight";
                diagnostics = PipelineDiagnostics.Run(request with { Grass = grass, Textures = textures, Terrain = terrain, Lod = lod }, progress: progress, ct: ct);
                diagnostics.Write(Path.Combine(job.Folder, "diagnostics.json"));
                if (!diagnostics.CanGenerate) throw new InvalidDataException("Preflight found blocking issues: " + string.Join("; ", diagnostics.Issues.Where(i => i.Severity == "Error").Select(i => i.Message)));
            }
            if (grass is not null)
            {
                active = "grass";
                grassResult = job.Run("grass", Fingerprint(grass, grass.Executable ?? GrassGeneration.BundledExecutable, grass.NgioConfig), () => GrassGeneration.Run(grass, progress, ct), r =>
                {
                    var manifest = GrassGenerationManifest.Read(r.OutputFolder, requireFull: !r.Preview)!;
                    return Record("grass", r.OutputFolder, ManifestSnapshot(r.OutputFolder, GrassGenerationManifest.FileName, manifest.Files, ct), !r.Preview);
                });
            }
            if (textures is not null)
            {
                active = "textures";
                textureResult = job.Run("textures", Fingerprint(textures, ResourceCatalog.ExternalFiles(textures.DynDolodFolder, textures.ResourcePacks).ToArray()), () =>
                {
                    var result = TextureGeneration.Run(textures, progress, ct);
                    textureResult = result;
                    if (!result.Published) throw new InvalidDataException("Texture generation did not publish a complete result. Review the texture report; later stages were not started.");
                    return result;
                }, r => Record("textures", r.OutputFolder, ManifestSnapshot(r.OutputFolder, TextureGenerationManifest.FileName,
                    TextureGenerationManifest.Read(r.OutputFolder, requireComplete: textures.ModelFilter.Length == 0)!.Files, ct), textures.ModelFilter.Length == 0));
            }
            if (terrain is not null)
            {
                active = "terrain";
                string terrainFingerprint=Fingerprint(terrain);
                terrainResult = job.Run("terrain", terrainFingerprint, () => TerrainGeneration.RunWithCheckpoints(terrain,
                    Path.Combine(job.Folder,"terrain-tiles"),terrainFingerprint,request.Resume,progress,ct), r => Record("terrain", r.OutputFolder, r.Output));
            }
            if (lod is not null)
            {
                active = "lod";
                OutputSnapshot? output = null;
                var previousLodHashes = job.Journal.Stages.Single(s => s.Plan.Id == "lod").Output;
                receipt = job.Run("lod", Fingerprint(lod, ResourceCatalog.ExternalFiles(lod.DynDolodFolder, lod.ResourcePacks).Concat(new[] { lod.CustomRulesFile,
                    grass is null ? lod.GeneratedGrassFolder : null, textures is null ? lod.GeneratedTextureFolder : null, terrain is null ? lod.GeneratedTerrainFolder : null }).ToArray()), () =>
                {
                    // The engine verifies generated inputs before writing to the isolated destination.
                    using var staged = new StagedOutput(lod.OutputFolder!, !lod.CleanOutput, progress, ct, deferLodBlocks: true, previousHashes: previousLodHashes);
                    var clock = Stopwatch.StartNew();
                    try { summary = ScanPipeline.Run(lod with { OutputFolder = staged.Payload }, progress, ct, staged); }
                    finally
                    {
                        var log = Path.Combine(staged.Payload, "AnvilLOD.log");
                        if (File.Exists(log)) File.Copy(log, Path.Combine(job.Folder, "lod.log"), true);
                    }
                    ScanReport.Write(Path.Combine(job.Folder, "scan-report.json"), summary, clock.Elapsed);
                    if (summary.Generation is { BlocksFailed: > 0 }) throw new InvalidDataException($"LOD generation had {summary.Generation.BlocksFailed} failed blocks. Previous output was preserved.");
                    if (summary.Grids.Count == 0) throw new InvalidDataException("No matching LOD worldspaces were found. Previous output was preserved.");
                    var retainedWorlds = OutputSetReceipt.Scope(OutputFiles.Enumerate(staged.Payload, ct)).Worlds;
                    if (retainedWorlds.Except(summary.Grids.Keys, StringComparer.OrdinalIgnoreCase).Any())
                        throw new InvalidDataException("Previous output includes worlds no longer found in this source. Generate into a new output folder to avoid retaining blocks with mismatched shared files.");
                    var costs = OutputCostReport.Analyze(staged.Payload, OutputCostReport.GenerationSettings(lod), progress, ct);
                    costs.Write(Path.Combine(staged.Payload, OutputCostReport.FileName));
                    costs.Write(Path.Combine(job.Folder, OutputCostReport.FileName));
                    progress?.Report(costs.Summary());
                    using (WorkTiming.Measure("output-verification")) output = OutputSnapshot.Capture(staged.Payload, progress, ct);
                    var dependencies = new List<OutputDependency>();
                    AddDependency("grass", lod.GeneratedGrassFolder);
                    AddDependency("textures", lod.GeneratedTextureFolder);
                    AddDependency("terrain", lod.GeneratedTerrainFolder);
                    // Record completed scope even when all blocks in a selected band became empty.
                    var worlds = summary.Grids.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();
                    var levels = (lod.Levels ?? AnvilLOD.Core.World.LodLevels.All).Select(l => (int)l).Order().ToArray();
                    var completedScope = new OutputScope(worlds, levels, worlds.SelectMany(w => levels.Select(l => w.ToLowerInvariant() + ":" + l)).Order(StringComparer.Ordinal).ToArray());
                    output = OutputSetReceipt.Record(staged.Payload, "lod", inputs.Fingerprint, output, dependencies, scope: completedScope);
                    staged.Commit(progress);
                    return new LodStageReceipt(lod.OutputFolder!, output.Files.Count, summary.Generation?.BlocksWritten ?? 0,
                        summary.Plan.Unchanged.Count, summary.Generation?.Triangles ?? 0, summary.Warnings, staged.PreviousOutput);

                    void AddDependency(string id, string? folder)
                    {
                        if (folder is null) return;
                        var saved = job.Journal.Stages.FirstOrDefault(s => s.Plan.Id == id)?.Output;
                        var snapshot = saved ?? (id switch
                        {
                            "textures" => ManifestSnapshot(folder, TextureGenerationManifest.FileName, TextureGenerationManifest.Read(folder)!.Files, ct),
                            "grass" => ManifestSnapshot(folder, GrassGenerationManifest.FileName, GrassGenerationManifest.Read(folder)!.Files, ct),
                            _ => TerrainGeneration.ReadManifest(folder)
                        });
                        dependencies.Add(new(id, snapshot.Files.Where(f => !f.Key.Equals(OutputSetReceipt.FileName, StringComparison.OrdinalIgnoreCase)).ToDictionary(f => f.Key, f => f.Value)));
                    }
                }, _ => output!, allowReuse: !lod.CleanOutput);
            }
            if (SeasonalCoverage.Requested(request))
            {
                active="seasonal-report";
                seasonalOutput=SeasonalCoverage.Outputs(request,ct);
                File.WriteAllText(Path.Combine(job.Folder,"seasonal-output.json"),JsonSerializer.Serialize(seasonalOutput,PersistentPipeline.Json));
                foreach(var season in seasonalOutput) progress?.Report($"{season.Season} output: {season.TerrainTiles} terrain tiles, {season.ObjectBlocks} object blocks, {season.TreeBlocks} tree blocks, {season.GrassCells} grass cells.");
            }
            active = null;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        { error = e.Message; progress?.Report("Pipeline stopped at " + active + ": " + e); }
        return new(job.Folder, inputs, job.Journal, grassResult, textureResult, receipt, summary, active, error, terrainResult, diagnostics, seasonalOutput);

        OutputSnapshot Record(string id, string folder, OutputSnapshot snapshot, bool complete = true)
        {
            // A Windows lookup can return the requested spelling rather than the directory's casing.
            // Do not add that same game asset again when collecting on-disk metadata.
            snapshot = new OutputSnapshot { Files = new(snapshot.Files, StringComparer.OrdinalIgnoreCase) };
            // Include stage reports and logs as owned metadata as well as the generated assets.
            foreach (string relative in OutputFiles.Enumerate(folder, ct))
                if (!relative.Equals(OutputSetReceipt.FileName, StringComparison.OrdinalIgnoreCase) && !relative.Equals("meta.ini", StringComparison.OrdinalIgnoreCase) && !snapshot.Files.ContainsKey(relative))
                    snapshot.Files[relative] = OutputSnapshot.HashFile(OutputSnapshot.Resolve(folder, relative), ct);
            return OutputSetReceipt.Record(folder, id, inputs.Fingerprint, snapshot, complete: complete);
        }
    }

    private static OutputSnapshot ManifestSnapshot(string root, string manifest, Dictionary<string, string> files, CancellationToken ct)
    {
        var result = new OutputSnapshot();
        var lookup = new GamePathLookup(root);
        foreach (var (relative, hash) in files)
        {
            ct.ThrowIfCancellationRequested();
            string file = lookup.ResolveExisting(relative, ct) ?? throw new FileNotFoundException("Published stage output is missing: " + relative);
            result.Files.Add(Path.GetRelativePath(root, file).Replace('\\', '/'), hash);
        }
        result.Files.Add(manifest, OutputSnapshot.HashFile(Path.Combine(root, manifest), ct));
        return result;
    }
}
