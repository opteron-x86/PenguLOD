using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using AnvilLOD.Core.Paths;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.Textures;
using AnvilLOD.Core.World;
using AnvilLOD.Textures;

namespace AnvilLOD.Plugins;

public sealed record TextureGenerationRequest(GameContextOptions Game, string OutputFolder, string? DynDolodFolder = null,
    bool Stitched = true, bool Trees = true, bool Grass = false, bool RenderedObjects = false,
    int BaseSize = 256, int BillboardMaxSize = 512, float UnitsPerPixel = 12, float Brightness = 1,
    float MinTreeHeight = 256, float MinGrassHeight = 16, string ModelFilter = "", bool TreeTrunks = false, Authoring.MaterialBakeSettings? Materials = null);
public sealed record TextureGenerationItem(string Kind, string Source, string Output, string Status, string Detail = "", int Width = 0, int Height = 0);
public sealed record TextureGenerationResult(string OutputFolder, bool Published, int Files, string Fingerprint,
    IReadOnlyList<TextureGenerationItem> Items, IReadOnlyList<string> Warnings, TimeSpan Elapsed)
{
    public int Errors => Items.Count(i => i.Status == "Failed");
}

/// <summary>Native texture preparation shared by the desktop and automation. No game or source mod is modified.</summary>
public static partial class TextureGeneration
{
    public static void Validate(TextureGenerationRequest request)
    {
        if (!request.Stitched && !request.Trees && !request.Grass && !request.RenderedObjects && !request.TreeTrunks)
            throw new ArgumentException("Select at least one kind of texture to generate.");
        if (request.BaseSize is < 64 or > 1024 || (request.BaseSize & (request.BaseSize - 1)) != 0
            || request.BillboardMaxSize is < 32 or > 2048 || (request.BillboardMaxSize & (request.BillboardMaxSize - 1)) != 0)
            throw new ArgumentException("Texture sizes must be powers of two: base 64–1024, billboards 32–2048.");
        if (!float.IsFinite(request.UnitsPerPixel) || request.UnitsPerPixel is < .25f or > 128
            || !float.IsFinite(request.Brightness) || request.Brightness is < .1f or > 4
            || !float.IsFinite(request.MinTreeHeight) || request.MinTreeHeight < 0
            || !float.IsFinite(request.MinGrassHeight) || request.MinGrassHeight < 0)
            throw new ArgumentException("Invalid texture resolution, brightness or minimum model height.");
        request.Materials?.Validate();
        GenerationDestination.Validate(request.Game, request.OutputFolder);
        if (File.Exists(Path.Combine(request.OutputFolder, TerrainGeneration.ManifestName)))
            throw new ArgumentException("Choose a separate output folder; this contains generated terrain.");
        if (File.Exists(Path.Combine(request.OutputFolder, GrassGenerationManifest.FileName)))
            throw new ArgumentException("Choose a separate texture output folder; this folder contains a generated grass cache.");
        if (File.Exists(Path.Combine(request.OutputFolder, "AnvilLOD.manifest.json")) || File.Exists(Path.Combine(request.OutputFolder, "AnvilLOD.log")))
            throw new ArgumentException("Choose a separate texture output folder; this folder contains generated LOD.");
        if (request.DynDolodFolder is { Length: > 0 } resources)
        {
            if (!Directory.Exists(resources)) throw new DirectoryNotFoundException("DynDOLOD resource folder not found: " + resources);
            if (HostPath.IsWithin(request.OutputFolder, resources) || HostPath.IsWithin(resources, request.OutputFolder))
                throw new ArgumentException("Texture output and DynDOLOD resources must be separate folders.");
        }
        var previous = TextureGenerationManifest.Read(request.OutputFolder);
        if (request.ModelFilter.Length > 0 && previous is { Complete: true, ModelFilter.Length: 0 })
            throw new ArgumentException("Choose a different output folder for a filtered preview; it must not replace your full texture set.");
    }

    public static TextureGenerationResult Run(TextureGenerationRequest request, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        Validate(request); ct.ThrowIfCancellationRequested();
        using var preparationTiming = WorkTiming.Measure("preparation");
        using var limits = RunLimits.Enter(RunLimits.Parallel(ct).MaxDegreeOfParallelism, ct, RunLimits.CacheDirectory);
        var timer = Stopwatch.StartNew();
        var items = new List<TextureGenerationItem>();
        var warnings = new List<string>();
        void Warn(string message) { warnings.Add(message); progress?.Report("WARNING: " + message); }
        using var game = GameContext.Open(request.Game with { ExcludeFolder = request.OutputFolder, ExtraLooseRoots = ["textures", "dyndolod"] }, progress);
        if (game.MissingPlugins.Count > 0) throw new InvalidOperationException("Resolve missing plugins before generating textures: " + string.Join(", ", game.MissingPlugins.Take(5)));
        foreach (var warning in game.Warnings) Warn(warning);
        var assets = game.BuildAssets(p => p.StartsWith("textures\\") || p.StartsWith("meshes\\") || p.StartsWith("dyndolod\\"));
        foreach (var skipped in assets.SkippedArchives) Warn("Archive skipped: " + skipped);
        progress?.Report($"Texture generation: {game.PluginsLoaded:N0} plugins, {assets.ArchiveCount:N0} archives.");
        string? resources = ResourceRoot(request.DynDolodFolder);
        var rules = ReadRules(game, assets, resources);
        if (request.Stitched && rules.Program.Entries.Count == 0)
            throw new InvalidOperationException("No texture stitching recipes match this load order. Choose the DynDOLOD installation folder, or turn off stitched textures.");
        rules.Program.BaseSize = request.BaseSize; rules.Program.Brightness = request.Brightness;
        if (request.RenderedObjects && resources is null && !assets.EnumeratePaths(@"dyndolod\render\skyrim\objects\").Any())
            throw new InvalidOperationException("Rendered objects need DynDOLOD render models. Choose the DynDOLOD installation folder.");

        Directory.CreateDirectory(request.OutputFolder);
        string lockPath = Path.Combine(request.OutputFolder, ".AnvilLOD.texture.lock");
        if (new FileInfo(lockPath).LinkTarget is not null) throw new IOException("The texture output lock cannot be a symbolic link.");
        using var outputLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var previous = TextureGenerationManifest.Read(request.OutputFolder);
        if (previous is null) new TextureGenerationManifest().Save(request.OutputFolder);
        string stage = Path.Combine(request.OutputFolder, ".texture-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        bool keepStage = false;
        try
        {
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var temporary = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Write(string relative, byte[] bytes)
            {
                ct.ThrowIfCancellationRequested();
                relative = GamePath.Normalize(relative); GamePath.ValidateRelative(relative);
                if (!(relative.StartsWith("textures\\") || relative.StartsWith("dyndolod-temp\\textures\\"))) throw new InvalidDataException("Output must be under textures: " + relative);
                string path = GamePath.ToFileSystemPath(stage, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using (WorkTiming.Measure("file-write-worker")) File.WriteAllBytes(path, bytes);
                if (relative.StartsWith("textures\\")) files[relative] = Convert.ToHexString(SHA256.HashData(bytes));
                else temporary.Add(relative);
            }
            byte[]? Read(string relative)
            {
                relative = GamePath.Normalize(relative);
                if (files.ContainsKey(relative) || temporary.Contains(relative)) return File.ReadAllBytes(GamePath.ToFileSystemPath(stage, relative));
                return ReadSource(relative);
            }
            byte[]? ReadSource(string relative)
            {
                if (!assets.TryOpen(relative, out var stream)) return null;
                using (stream) { using var bytes = new MemoryStream(); stream.CopyTo(bytes); return bytes.ToArray(); }
            }
            preparationTiming.Dispose();
            using var generationTiming = WorkTiming.Measure("generation");
            var recipeInputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (request.Stitched)
                foreach (var conversion in rules.Program.Entries.Select(e => e.Conversion).OfType<TextureConversion>())
                    if (conversion.Source.Split('|').Contains(conversion.Destination, StringComparer.OrdinalIgnoreCase)
                        && ReadSource(conversion.Destination) is { } original)
                    {
                        string saved = @"textures\pengulod\recipe-inputs\" + Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant() + ".dds";
                        Write(saved, original); recipeInputs[conversion.Destination] = saved;
                    }
            if (request.Stitched) Stitch(rules.Program, ReadSource, Read, Write, items, Warn, progress, ct);
            if (request.Trees || request.Grass) GenerateBillboards(request, game, assets, rules.Billboards, Read, Write, items, Warn, progress, ct);
            if (request.TreeTrunks) GenerateTrunks(request, assets, resources, Read, Write, items, Warn, progress, ct);
            if (request.RenderedObjects) RenderObjects(request, assets, resources, Read, Write, items, Warn, progress, ct);
            ct.ThrowIfCancellationRequested();
            progress?.Report($"Texture results: {items.Count(i => i.Status == "Written"):N0} generated, " +
                $"{items.Count(i => i.Status == "Retained"):N0} retained, {items.Count(i => i.Status == "Skipped"):N0} skipped, " +
                $"{items.Count(i => i.Status == "Failed"):N0} failed.");
            generationTiming.Dispose();
            using var publicationTiming = WorkTiming.Measure("publication");
            bool publish = files.Count > 0 && items.All(i => i.Status != "Failed");
            string fingerprint = "";
            if (publish)
            {
                // Preflight every collision and stale file before replacing any published output.
                foreach (var relative in files.Keys.Union(previous?.Files.Keys.AsEnumerable() ?? [], StringComparer.OrdinalIgnoreCase))
                {
                    var target = GamePath.ToFileSystemPath(request.OutputFolder, relative);
                    if (!HostPath.IsWithin(target, request.OutputFolder) || new FileInfo(target).LinkTarget is not null) throw new IOException("Output path escapes through a symlink: " + target);
                    if (Directory.Exists(target)) throw new IOException("A directory is occupying an output filename: " + target);
                    if (File.Exists(target) && (previous is null || !previous.Files.TryGetValue(relative, out var hash)
                        || Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target))) != hash))
                        throw new IOException("An output file was added or changed outside texture generation; choose an empty output folder: " + target);
                }
                var manifest = new TextureGenerationManifest { Source = game.SourceDescription, ModelFilter = request.ModelFilter, GeneratedAt = DateTimeOffset.Now, Files = files, Recipes = request.Stitched ? rules.Program : null, RecipeInputs = recipeInputs };
                var touched = files.Keys.Union(previous?.Files.Keys.AsEnumerable() ?? [], StringComparer.OrdinalIgnoreCase).ToArray();
                string backup = Path.Combine(stage, "rollback");
                foreach (var relative in previous?.Files.Keys.AsEnumerable() ?? [])
                {
                    var source = GamePath.ToFileSystemPath(request.OutputFolder, relative);
                    if (!File.Exists(source)) continue;
                    var saved = GamePath.ToFileSystemPath(backup, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(saved)!); File.Copy(source, saved);
                }
                ct.ThrowIfCancellationRequested();
                manifest.Save(request.OutputFolder); // Cancellation is deferred during publication; an interrupted process leaves an incomplete manifest.
                try
                {
                    foreach (var relative in files.Keys)
                    {
                        var target = GamePath.ToFileSystemPath(request.OutputFolder, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        File.Move(GamePath.ToFileSystemPath(stage, relative), target, true);
                    }
                    foreach (var stale in previous?.Files.Keys.Except(files.Keys, StringComparer.OrdinalIgnoreCase) ?? [])
                        File.Delete(GamePath.ToFileSystemPath(request.OutputFolder, stale));
                    manifest.Complete = true; manifest.Save(request.OutputFolder); fingerprint = manifest.Fingerprint;
                }
                catch
                {
                    try
                    {
                        foreach (var relative in touched)
                        {
                            var saved = GamePath.ToFileSystemPath(backup, relative);
                            var target = GamePath.ToFileSystemPath(request.OutputFolder, relative);
                            if (File.Exists(saved)) File.Copy(saved, target, true);
                            else File.Delete(target);
                        }
                        (previous ?? new TextureGenerationManifest()).Save(request.OutputFolder);
                    }
                    catch (Exception rollback)
                    {
                        keepStage = true;
                        throw new IOException("Publication and rollback failed. Previous files are preserved in " + backup + ". Use a fresh output folder. " + rollback.Message, rollback);
                    }
                    throw;
                }
                progress?.Report($"Published {files.Count:N0} texture and billboard files to {request.OutputFolder}.");
            }
            else progress?.Report("Texture output was not published. Review failures; any previous completed output was kept.");
            var result = new TextureGenerationResult(request.OutputFolder, publish, files.Count, fingerprint, items, warnings, timer.Elapsed);
            File.WriteAllText(Path.Combine(request.OutputFolder, "AnvilLOD.textures-report.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            return result;
        }
        finally { if (!keepStage && Directory.Exists(stage)) Directory.Delete(stage, true); }
    }

    internal static string? ResourceRoot(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return null;
        foreach (var sub in new[] { @"Edit Scripts\DynDOLOD", "DynDOLOD", "" })
        {
            var root = sub.Length == 0 ? folder : GamePath.ResolveExisting(folder, sub);
            if (root is not null && (GamePath.ResolveExisting(root, "Configs") is not null || GamePath.ResolveExisting(root, "Render") is not null)) return root;
        }
        throw new DirectoryNotFoundException("Choose the DynDOLOD folder containing Edit Scripts/DynDOLOD/Configs.");
    }

    internal sealed record Recipes(TextureRecipeSet Program, List<string> Billboards);
    internal static Recipes ReadRules(GameContext game, AssetIndex assets, string? resources)
    {
        var sources = new Dictionary<string, Func<string>>(StringComparer.OrdinalIgnoreCase);
        if (resources is not null && GamePath.ResolveExisting(resources, "Configs") is { } configs)
            foreach (var file in Directory.EnumerateFiles(configs).Order(StringComparer.OrdinalIgnoreCase))
                sources[Path.GetFileName(file)] = () => File.ReadAllText(file);
        foreach (var path in assets.EnumeratePaths("dyndolod\\").Order(StringComparer.OrdinalIgnoreCase))
        {
            if (path[9..].Contains('\\')) continue;
            sources[GamePath.GetFileName(path)] = () =>
            {
                if (!assets.TryOpen(path, out var stream)) throw new FileNotFoundException(path);
                using (stream) using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
            };
        }
        var tokens = game.LoadOrder.ListedOrder.Select((p, i) => (Token: TextureRecipe.PluginToken(p.ModKey.FileName.String), Index: i)).ToDictionary(p => p.Token, p => p.Index, StringComparer.OrdinalIgnoreCase);
        var result = new Recipes(new(), []);
        var matched = sources.Select(s => (s.Key, s.Value, Match: System.Text.RegularExpressions.Regex.Match(s.Key,
            @"^DynDOLOD_(?:SSE|TES5VR)_TexGen_(copy|alpha|noalpha|Billboards)_(.+)\.txt$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
            .Where(s => s.Match.Success && tokens.ContainsKey(s.Match.Groups[2].Value))
            .OrderBy(s => tokens[s.Match.Groups[2].Value]).ThenBy(s => s.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, read, match) in matched)
        {
            string kind = match.Groups[1].Value.ToLowerInvariant();
            int row = 0;
            foreach (var line in TextureRecipe.Lines(read()))
            {
                row++;
                try
                {
                    if (kind == "copy") result.Program.Entries.Add(new(name, row, Copy: TextureRecipe.ParseCopy(line)));
                    else if (kind == "billboards") result.Billboards.Add(line);
                    else
                    {
                        var columns = line.Split('\t', StringSplitOptions.TrimEntries);
                        if (columns.Length == 12 && columns[5].Equals("x", StringComparison.OrdinalIgnoreCase))
                            result.Program.Entries.Add(new(name, row, Conversion: TextureRecipe.ParseConversion(line, kind == "alpha")));
                        else result.Program.Entries.Add(new(name, row, Tile: TextureRecipe.ParseTile(line, kind == "alpha")));
                    }
                }
                catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
                { throw new InvalidDataException($"{name}, rule {row}: {ex.Message}", ex); }
            }
        }
        return result;
    }

    private static void Stitch(TextureRecipeSet recipes, Func<string, byte[]?> readSource, Func<string, byte[]?> readGenerated,
        Action<string, byte[]> write, List<TextureGenerationItem> items, Action<string> warn, IProgress<string>? progress, CancellationToken ct)
    {
        var renderer = new TextureRecipeRenderer(recipes, readSource, texture =>
        {
            write(texture.Destination, texture.Bytes);
            if (texture.Kind != "Normal") items.Add(new(texture.Kind, texture.Sources, texture.Destination,
                texture.Destination.StartsWith("dyndolod-temp\\") ? "Intermediate" : "Written", "", texture.Width, texture.Height));
        }, warn, readGenerated: readGenerated);
        int index = 0;
        foreach (var output in renderer.Plans.Keys)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"Preparing texture {++index}/{renderer.Plans.Count}: {output}");
            try { renderer.Build(output); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { items.Add(new("Recipe", "", output, "Failed", ex.Message)); progress?.Report("ERROR: " + ex.Message); }
        }
    }
}
