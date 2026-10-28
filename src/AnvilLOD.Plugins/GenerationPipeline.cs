using System.Diagnostics;
using System.Text.Json;
using AnvilLOD.Core.Paths;
using AnvilLOD.Core.World;
using AnvilLOD.Core.Pipeline;

namespace AnvilLOD.Plugins;

public sealed record GenerationPipelineRequest(GrassGenerationRequest? Grass = null, TextureGenerationRequest? Textures = null,
    ScanRequest? Lod = null, bool Resume = true, bool VerifySourceContents = false, TerrainGenerationRequest? Terrain = null, bool Preflight = true);
public sealed record LodStageReceipt(string OutputFolder, int Files, int BlocksWritten, int BlocksUnchanged,
    long Triangles, IReadOnlyList<string> Warnings, string? PreviousOutput);
public sealed record GenerationPipelineResult(string JobFolder, PipelineInputSnapshot Inputs, PipelineJournal Journal,
    GrassGenerationResult? Grass, TextureGenerationResult? Textures, LodStageReceipt? LodReceipt, ScanSummary? Lod,
    string? FailedStage, string? Error, TerrainGenerationResult? Terrain = null, DiagnosticReport? Diagnostics = null)
{
    public bool Success => Error is null;
}

/// <summary>Shared persistent orchestration for desktop and CLI. Generated inputs are explicit dependencies.</summary>
public static class GenerationPipeline
{
    public static GenerationPipelineResult Run(GenerationPipelineRequest request, string cache,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var source = request.Grass?.Game ?? request.Textures?.Game ?? request.Lod?.Game ?? request.Terrain?.Game
            ?? throw new ArgumentException("Select at least one pipeline stage.");
        var outputs = new[] { request.Grass?.OutputFolder, request.Textures?.OutputFolder, request.Lod?.OutputFolder, request.Terrain?.OutputFolder }.OfType<string>().ToArray();
        foreach (var output in outputs)
        {
            GenerationDestination.Validate(source, output);
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
        string? active = null, error = null;
        string Fingerprint<T>(T value, params string?[] extra) => inputs.Fingerprint + "|" + JsonSerializer.Serialize(value)
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
                    return ManifestSnapshot(r.OutputFolder, GrassGenerationManifest.FileName, manifest.Files, ct);
                });
            }
            if (textures is not null)
            {
                active = "textures";
                textureResult = job.Run("textures", Fingerprint(textures, textures.DynDolodFolder), () =>
                {
                    var result = TextureGeneration.Run(textures, progress, ct);
                    textureResult = result;
                    if (!result.Published) throw new InvalidDataException("Texture generation did not publish a complete result. Review the texture report; later stages were not started.");
                    return result;
                }, r => ManifestSnapshot(r.OutputFolder, TextureGenerationManifest.FileName,
                    TextureGenerationManifest.Read(r.OutputFolder, requireComplete: textures.ModelFilter.Length == 0)!.Files, ct));
            }
            if (terrain is not null)
            {
                active = "terrain";
                terrainResult = job.Run("terrain", Fingerprint(terrain), () => TerrainGeneration.Run(terrain, progress, ct), r => r.Output);
            }
            if (lod is not null)
            {
                active = "lod";
                OutputSnapshot? output = null;
                receipt = job.Run("lod", Fingerprint(lod, lod.DynDolodFolder, lod.CustomRulesFile,
                    grass is null ? lod.GeneratedGrassFolder : null, textures is null ? lod.GeneratedTextureFolder : null, terrain is null ? lod.GeneratedTerrainFolder : null), () =>
                {
                    // The engine verifies generated inputs before writing to the isolated destination.
                    using var staged = new StagedOutput(lod.OutputFolder!, !lod.CleanOutput, progress, ct);
                    var clock = Stopwatch.StartNew();
                    try { summary = ScanPipeline.Run(lod with { OutputFolder = staged.Payload }, progress, ct); }
                    finally
                    {
                        var log = Path.Combine(staged.Payload, "AnvilLOD.log");
                        if (File.Exists(log)) File.Copy(log, Path.Combine(job.Folder, "lod.log"), true);
                    }
                    ScanReport.Write(Path.Combine(job.Folder, "scan-report.json"), summary, clock.Elapsed);
                    if (summary.Generation is { BlocksFailed: > 0 }) throw new InvalidDataException($"LOD generation had {summary.Generation.BlocksFailed} failed blocks. Previous output was preserved.");
                    if (summary.Grids.Count == 0) throw new InvalidDataException("No matching LOD worldspaces were found. Previous output was preserved.");
                    using (WorkTiming.Measure("output-verification")) output = OutputSnapshot.Capture(staged.Payload, progress, ct);
                    staged.Commit(progress);
                    return new LodStageReceipt(lod.OutputFolder!, output.Files.Count, summary.Generation?.BlocksWritten ?? 0,
                        summary.Plan.Unchanged.Count, summary.Generation?.Triangles ?? 0, summary.Warnings, staged.PreviousOutput);
                }, _ => output!, allowReuse: !lod.CleanOutput);
            }
            active = null;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        { error = e.Message; progress?.Report("Pipeline stopped at " + active + ": " + e); }
        return new(job.Folder, inputs, job.Journal, grassResult, textureResult, receipt, summary, active, error, terrainResult, diagnostics);
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
