using System.Globalization;
using System.Numerics;
using System.Text;
using AnvilLOD.Core.World;
using AnvilLOD.Meshes;
using AnvilLOD.Meshes.Authoring;
using AnvilLOD.Meshes.Nif;
using AnvilLOD.Plugins.Authoring;
using AnvilLOD.Textures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace AnvilLOD.Plugins;

public static partial class TextureGeneration
{
    private sealed record Candidate(string Kind, string Plugin, IMajorRecordGetter Record);
    private sealed record BillboardRule(bool? Enabled, float? Scale);

    private static void GenerateBillboards(TextureGenerationRequest request, GameContext game, AssetIndex assets,
        List<string> ruleLines, Func<string, byte[]?> read, Action<string, byte[]> write,
        List<TextureGenerationItem> items, Action<string> warn, IProgress<string>? progress, CancellationToken ct)
    {
        var rules = new Dictionary<FormKey, BillboardRule>();
        foreach (var line in ruleLines)
        {
            var fields = line.Split(';', StringSplitOptions.TrimEntries);
            if (fields.Length < 2 || !uint.TryParse(fields[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id))
                throw new InvalidDataException("Invalid billboard rule: " + line);
            bool? enabled = null; float? scale = null;
            foreach (var option in fields.Skip(2).SelectMany(f => f.Split(',', StringSplitOptions.TrimEntries)))
            {
                if (bool.TryParse(option, out var b)) enabled = b;
                if (option.StartsWith("Scale=", StringComparison.OrdinalIgnoreCase))
                {
                    if (!float.TryParse(option[6..], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !float.IsFinite(value) || value <= 0 || value > 100)
                        throw new InvalidDataException("Invalid billboard scale: " + line);
                    scale = value;
                }
            }
            rules[new FormKey(ModKey.FromNameAndExtension(fields[0]), id & 0x00ffffff)] = new(enabled, scale);
        }
        var records = new Dictionary<FormKey, Candidate>();
        foreach (var listing in game.LoadOrder.ListedOrder)
        {
            ct.ThrowIfCancellationRequested();
            if (listing.Mod is not { } mod) continue;
            void Add(string kind, IMajorRecordGetter record) => records[record.FormKey] = new(kind, listing.ModKey.FileName.String, record);
            if (request.Trees)
            {
                foreach (var tree in mod.Trees) Add("Tree", tree);
                // Include all overrides first, then check the winning record's flags.
                foreach (var tree in mod.Statics) Add("Static tree", tree);
            }
            if (request.Grass) foreach (var grass in mod.Grasses) Add("Grass", grass);
        }
        var candidates = records.Values.Where(c => !c.Record.IsDeleted && (c.Record is not IStaticGetter s
            || s.MajorFlags.HasFlag(Static.MajorFlag.HasTreeLOD) || rules.ContainsKey(s.FormKey))).ToArray();
        // Grass libraries often contain obsolete definitions whose textures are intentionally absent.
        // Include every winning LTEX, including seasonal replacement textures, not just placed terrain.
        var usedGrass = new HashSet<FormKey>();
        if (request.Grass)
            foreach (var landscape in game.LoadOrder.PriorityOrder.LandscapeTexture().WinningOverrides())
            {
                ct.ThrowIfCancellationRequested();
                if (!landscape.IsDeleted) foreach (var grass in landscape.Grasses) usedGrass.Add(grass.FormKey);
            }
        var lists = new Dictionary<string, List<string>> { ["Tree"] = [], ["Grass"] = [] };
        var quality = request.Billboards ?? new();
        var scales = quality.ReferenceScale && request.Trees
            ? ReferenceScales(game, candidates.Where(c => c.Kind != "Grass").Select(c => c.Record.FormKey).ToHashSet(), progress, ct)
            : new Dictionary<FormKey, float>();
        var loader = TextureLoader(read);
        var grassLoader = TextureLoader(read, preserveResolution: quality.GrassTextures != GrassTextureMode.Basic);
        int index = 0;
        foreach (var candidate in candidates.OrderBy(c => c.Record.FormKey.ToString(), StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var record = candidate.Record;
            if ((record as IModeledGetter)?.Model is not { File.IsNull: false } model) continue;
            var path = GamePath.Normalize(model.File.DataRelativePath.Path);
            if (!path.StartsWith("meshes\\")) path = "meshes\\" + path;
            if (request.ModelFilter.Length > 0 && !path.Contains(request.ModelFilter, StringComparison.OrdinalIgnoreCase)) continue;
            string stem = Path.GetFileNameWithoutExtension(GamePath.GetFileName(path));
            string target = $@"textures\terrain\lodgen\{candidate.Plugin.ToLowerInvariant()}\{stem}_{record.FormKey.ID:x8}";
            var rule = rules.GetValueOrDefault(record.FormKey);
            bool isGrass = candidate.Kind == "Grass";
            float scale = rule?.Scale ?? (isGrass ? 1 : scales.GetValueOrDefault(record.FormKey, quality.UnplacedTreeScale));
            var list = lists[isGrass ? "Grass" : "Tree"];
            string entry = $"{record.FormKey.ModKey};{record.FormKey.ID:X8};{record.EditorID}";
            progress?.Report($"Rendering billboard {++index}/{candidates.Length}: {path}");
            void Skip(string reason)
            {
                items.Add(new(candidate.Kind, path, target + ".dds", "Skipped", reason));
                list.Add(entry + ",False");
            }
            if (!isGrass && rule is null && !path.Contains("tree", StringComparison.OrdinalIgnoreCase)) { Skip("Model path is not a tree"); continue; }
            if (rule?.Enabled == false) { Skip("Disabled by billboard rule"); continue; }
            if (isGrass && rule is null && request.ModelFilter.Length == 0 && !usedGrass.Contains(record.FormKey))
            { Skip("Not referenced by any winning landscape texture; use a billboard rule or model filter to include it explicitly"); continue; }
            if (isGrass && record is IGrassGetter g && g.UnitsFromWaterType.ToString().StartsWith("Below", StringComparison.Ordinal))
            { Skip("Underwater grass"); continue; }
            try
            {
                var bytes = read(path);
                if (bytes is null) { Skip("Model file missing"); warn("Billboard model missing: " + path); continue; }
                var mesh = NifGeometryReader.Read(path, bytes, passthru: true, renderEffects: true);
                foreach (var warning in mesh.Warnings) warn(path + ": " + warning);
                // Automatic tree discovery also finds ghost/effect trees. Exclude the whole model
                // when only its effect materials are unsupported; never publish a partial crown.
                // A rule or filtered preview is an explicit render request and must still fail.
                if (!isGrass && rule is null && request.ModelFilter.Length == 0 && mesh.Warnings.Count > 0
                    && mesh.Warnings.All(w => w.StartsWith(NifGeometryReader.UnsupportedEffectWarning, StringComparison.Ordinal)))
                {
                    const string reason = "Excluded whole tree from automatic billboards: unsupported animated, blended or view-dependent effects. No partial billboard was generated; an explicit billboard rule or model filter requires a supported render.";
                    Skip(reason); warn(path + ": " + reason); continue;
                }
                if (mesh.HasIncompleteGeometry)
                { throw new NotSupportedException("Unsupported model shapes; existing output was preserved: " + string.Join("; ", mesh.Warnings)); }
                if (mesh.Parts.Count == 0) { Skip("Model has no supported shapes"); continue; }
                var bounds = LodMeshAuthor.Bounds(mesh);
                if (rule is null && bounds.Max.Z - bounds.Min.Z < (isGrass ? request.MinGrassHeight : request.MinTreeHeight))
                { Skip("Below minimum model height"); continue; }

                if (!TreeMaterials.TryApply(mesh, TreeMaterials.Read(game, model), out mesh, out var variantError))
                    throw new InvalidDataException(variantError);
                BillboardImage? Render(BillboardView view) => BillboardRenderer.Render(mesh, isGrass ? grassLoader : loader, request.BillboardMaxSize, vertexColors: !isGrass,
                    unitsPerPixel: request.UnitsPerPixel / scale, brightness: request.Brightness, materials: request.Materials,
                    view: view, originCentered: true, grassTextures: isGrass ? quality.GrassTextures : GrassTextureMode.Basic,
                    complexGrassThreshold: quality.ComplexGrassThreshold);
                var img = Render(BillboardView.Front)
                    ?? throw new InvalidDataException("The model rendered no visible pixels.");
                var side = quality.HdViews ? Render(BillboardView.Side) : null;
                if (quality.HdViews && side is null) warn($"{path}: the side view has no visible surface; crossed cards will reuse the front view.");
                var diffuseBytes = TextureImages.Encode(new(img.Rgba, img.Width, img.Height), alpha: true);
                write(target + ".dds", diffuseBytes); write(target + "_1.dds", diffuseBytes);
                write(target + "_1_n.dds", TextureImages.Encode(new(img.NormalRgba!, img.Width, img.Height), normal: true));
                if (side is not null)
                {
                    write(target + "_2.dds", TextureImages.Encode(new(side.Rgba, side.Width, side.Height), alpha: true));
                    write(target + "_2_n.dds", TextureImages.Encode(new(side.NormalRgba!, side.Width, side.Height), normal: true));
                }
                string metadata = FormattableString.Invariant($"[LOD]\r\nWidth={img.WorldWidth:F6}\r\nHeight={img.WorldHeight:F6}\r\nShiftX=0\r\nShiftY=0\r\nShiftZ={img.ShiftZ:F6}\r\nScale=1.000000\r\nWidth_1={img.WorldWidth:F6}\r\nHeight_1={img.WorldHeight:F6}\r\nModel={path}\r\nGenerator=AnvilLOD native textures\r\n");
                metadata += FormattableString.Invariant($"ResolutionScale={scale:R}\r\n");
                if (side is not null) metadata += FormattableString.Invariant($"Width_2={side.WorldWidth:F6}\r\nHeight_2={side.WorldHeight:F6}\r\n");
                write(target + ".txt", Encoding.UTF8.GetBytes(metadata));
                list.Add(entry + ",True");
                items.Add(new(candidate.Kind, path, target + ".dds", "Written", FormattableString.Invariant($"{record.FormKey}; resolution scale {scale:R}; {(side is null ? "front view" : "front and side views")}"), img.Width, img.Height));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { items.Add(new(candidate.Kind, path, target + ".dds", "Failed", ex.Message)); progress?.Report("ERROR: " + ex.Message); }
        }
        foreach (var (kind, entries) in lists.Where(l => l.Value.Count > 0))
            write($@"textures\terrain\lodgen\TexGen_SSE_{kind}_Billboards.txt", Encoding.UTF8.GetBytes(string.Join("\r\n", entries) + "\r\n"));
    }

    private static Dictionary<FormKey, float> ReferenceScales(GameContext game, HashSet<FormKey> candidates, IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report("Finding maximum placed tree scales for billboard resolution...");
        var result = new Dictionary<FormKey, float>();
        foreach (var reference in game.LoadOrder.PriorityOrder.PlacedObject().WinningOverrides())
        {
            ct.ThrowIfCancellationRequested();
            if (reference.IsDeleted || reference.Base.FormKeyNullable is not { } key || !candidates.Contains(key)) continue;
            float scale = reference.Scale ?? 1;
            if (float.IsFinite(scale) && scale > 0) result[key] = MathF.Max(result.GetValueOrDefault(key), scale);
        }
        progress?.Report($"Billboard resolution: reference scales found for {result.Count:N0} tree records.");
        return result;
    }

    private static BillboardRenderer.TextureLoader TextureLoader(Func<string, byte[]?> read, bool preserveResolution = false)
    {
        // Reuse diffuse inputs across records. A bounded FIFO prevents large profiles from retaining every decoded image.
        var cache = new Dictionary<string, (byte[], int, int)>(StringComparer.OrdinalIgnoreCase);
        var order = new Queue<string>(); long bytesHeld = 0;
        const long budget = 64L * 1024 * 1024;
        return path =>
        {
            path = GamePath.NormalizeTexture(path);
            if (cache.TryGetValue(path, out var cached)) return cached;
            var image = TextureImages.Decode(read(path) ?? throw new FileNotFoundException("Diffuse texture missing: " + path));
            // Normal alpha is gloss/specular, not opacity. Do not let it weight RGB filtering.
            if (path.EndsWith("_n.dds", StringComparison.OrdinalIgnoreCase))
                for (int i = 3; i < image.Pixels.Length; i += 4) image.Pixels[i] = 255;
            while (!preserveResolution && (image.Width > 1024 || image.Height > 1024))
            {
                var smaller = TreeAtlasBuilder.Downsample(image.Pixels, image.Width, image.Height);
                image = new(smaller.Rgba, smaller.W, smaller.H);
            }
            while (bytesHeld + image.Pixels.Length > budget && order.TryDequeue(out var oldest))
                if (cache.Remove(oldest, out var previous)) bytesHeld -= previous.Item1.Length;
            bytesHeld += image.Pixels.Length; order.Enqueue(path);
            return cache[path] = (image.Pixels, image.Width, image.Height);
        };
    }

    private static LodMeshPart CopyPart(LodMeshPart p, LodMaterial material) => new()
    {
        Name = p.Name, TexGenScale = p.TexGenScale, Material = material, Positions = p.Positions, UVs = p.UVs, Normals = p.Normals,
        Tangents = p.Tangents, Bitangents = p.Bitangents, Triangles = p.Triangles, Colors = p.Colors, Tier = p.Tier
    };

    private static void RenderObjects(TextureGenerationRequest request, AssetIndex assets, string? resources,
        Func<string, byte[]?> read, Action<string, byte[]> write, List<TextureGenerationItem> items,
        Action<string> warn, IProgress<string>? progress, CancellationToken ct)
    {
        const string prefix = @"dyndolod\render\skyrim\objects\";
        var models = new Dictionary<string, (Func<byte[]> Load, bool Optional)>(StringComparer.OrdinalIgnoreCase);
        if (resources is not null && GamePath.ResolveExisting(resources, @"render\skyrim\objects") is { } folder)
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Where(p => p.EndsWith(".nif", StringComparison.OrdinalIgnoreCase)))
                models[GamePath.Normalize(Path.GetRelativePath(folder, file))] = (() => File.ReadAllBytes(file), true);
        foreach (var path in assets.EnumeratePaths(prefix).Where(p => p.EndsWith(".nif")))
            models[path[prefix.Length..]] = (() => read(path) ?? throw new FileNotFoundException(path), false);
        if (models.Count == 0) throw new InvalidOperationException("No object render models were found.");
        int index = 0;
        foreach (var (path, model) in models.OrderBy(m => m.Key, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            if (request.ModelFilter.Length > 0 && !path.Contains(request.ModelFilter, StringComparison.OrdinalIgnoreCase)) continue;
            string destination = @"textures\" + path[..^4] + ".dds";
            progress?.Report($"Rendering object {++index}/{models.Count}: {path}");
            byte[]? existingDiffuse = null;
            bool optional = model.Optional && request.ModelFilter.Length == 0;
            try
            {
                // The standalone resource bundle covers other games and optional mods too.
                // Its templates update installed atlases. Mod-provided templates and explicit previews
                // can create new atlases and must report failures instead of silently falling back.
                if (optional && (existingDiffuse = read(destination)) is null)
                {
                    items.Add(new("Rendered object", path, destination, "Skipped", "Optional resource has no matching installed LOD texture"));
                    continue;
                }
                var mesh = NifGeometryReader.Read(path, model.Load(), includeTexGenPlane: true, passthru: true, renderEffects: true);
                foreach (var warning in mesh.Warnings) warn(path + ": " + warning);
                if (mesh.HasIncompleteGeometry)
                    throw new NotSupportedException("The render model contains unsupported shapes; incomplete object textures were not generated: " + string.Join("; ", mesh.Warnings));
                var plane = mesh.Parts.FirstOrDefault(p => p.Name?.Contains("TexGen Plane", StringComparison.OrdinalIgnoreCase) == true)
                    ?? throw new InvalidDataException("Render model has no supported TexGen Plane.");
                if (plane.VertexCount != 4) throw new InvalidDataException("TexGen Plane must have four vertices.");
                var viewport = LodMeshAuthor.Bounds(new LodMesh { Path = path, Parts = [plane] });
                var visible = new LodMesh { Path = path, Parts = mesh.Parts.Where(p => !ReferenceEquals(p, plane)).ToArray() };
                float width = viewport.Max.X - viewport.Min.X, height = viewport.Max.Z - viewport.Min.Z;
                if (!(width > 0) || !(height > 0)) throw new InvalidDataException("Degenerate TexGen viewport.");
                int outW = TextureImages.Size(plane.TexGenScale * Math.Max(1, MathF.Round(width / height)), request.BaseSize);
                int outH = TextureImages.Size(plane.TexGenScale * Math.Max(1, MathF.Round(height / width)), request.BaseSize);
                var image = BillboardRenderer.Render(visible, TextureLoader(read), request.BaseSize * 4,
                    unitsPerPixel: 1024f / request.BaseSize, viewport: viewport, brightness: request.Brightness, shaded: false, pixelSize: (outW, outH), materials: request.Materials)
                    ?? throw new InvalidDataException("The object rendered no visible pixels.");
                if (!plane.Material.HasAlpha) for (int i = 3; i < image.Rgba.Length; i += 4) image.Rgba[i] = 255;
                write(destination, TextureImages.Encode(new(image.Rgba, image.Width, image.Height), alpha: plane.Material.HasAlpha));
                write(destination[..^4] + "_n.dds", TextureImages.Encode(new(image.NormalRgba!, image.Width, image.Height), normal: true));
                items.Add(new("Rendered object", path, destination, "Written", "Source normals and calibrated albedo; view-dependent PBR lighting is not baked", image.Width, image.Height));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                string detail = ex.Message;
                if (optional && existingDiffuse is not null && ex is FileNotFoundException or NotSupportedException)
                {
                    try
                    {
                        string normalPath = destination[..^4] + "_n.dds";
                        var existingNormal = read(normalPath) ?? throw new FileNotFoundException("Existing normal map missing: " + normalPath);
                        var diffuse = TextureImages.Decode(existingDiffuse);
                        var normal = TextureImages.Decode(existingNormal);
                        if (diffuse.Width != normal.Width || diffuse.Height != normal.Height)
                            throw new InvalidDataException("Existing diffuse and normal map sizes do not match.");
                        write(destination, existingDiffuse); write(normalPath, existingNormal);
                        detail = "Retained existing diffuse and normal maps unchanged; native rendering unavailable: " + detail;
                        items.Add(new("Rendered object", path, destination, "Retained", detail, diffuse.Width, diffuse.Height));
                        warn(destination + ": " + detail);
                        continue;
                    }
                    catch (Exception fallback) when (fallback is not OperationCanceledException)
                    { detail += " Existing texture pair could not be retained: " + fallback.Message; }
                }
                items.Add(new("Rendered object", path, destination, "Failed", detail)); progress?.Report("ERROR: " + detail);
            }
        }
    }
}
