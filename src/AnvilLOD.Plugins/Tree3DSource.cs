using System.Collections.Concurrent;
using System.Numerics;
using AnvilLOD.Core.World;
using AnvilLOD.Meshes;
using AnvilLOD.Meshes.Nif;
using AnvilLOD.Meshes.Authoring;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.Lod;

namespace AnvilLOD.Plugins;

/// <summary>What 3D tree LOD did in a run (reported in the log and the summary).</summary>
public sealed record Tree3DStats(
    int TreesIn3D,
    int ModelTypes,
    int TypesByCrc,
    int TypesByName,
    int TypesSkipped,
    long TreesSkipped,
    int NameOnlyAvailable,          // types whose only model is under the plain name, with the option off
    IReadOnlyList<string> Warnings,
    long Triangles = 0,             // total across the selected near levels, including their card fallbacks
    double ApproxMegabytes = 0,
    IReadOnlyList<Tree3DLevelReport>? Levels = null, int NativeTypes = 0, int TotalTrees = 0);

public sealed record Tree3DLevelReport(string Model, int Level, string SourceModel, int Instances, int SourceTriangles,
    int OutputTriangles, long PlacedTriangles, double ApproxMegabytes, string Method, string? Fallback, string? Variant = null,
    int FoliageTrianglesRemoved = 0, float FoliageCoverageLoss = 0)
{
    public float FoliageCoveragePercent => FoliageCoverageLoss * 100;
}

/// <summary>
/// 3D tree LOD models (DynDOLOD's <c>…passthru_lod.nif</c>) placed in object LOD: real crown geometry plus trunk
/// billboards in one mesh, drawn at LOD4 (and LOD8 when asked), with billboard cards from there on.
/// <para>
/// A model is read with its shaders untouched (that is what "passthru" means), brightened with the tree LOD brightness,
/// and, for shapes named <c>spherenormals</c>, given normals that point away from the centre of the model. A model whose
/// textures can't be found (TexGen hasn't rendered its trunk billboards yet) is not used: the trees keep billboard LOD.
/// </para>
/// </summary>
public sealed class Tree3DSource : ISyntheticMeshSource
{
    public const string Prefix = "~anvillod-tree3d|";
    private const int Version = 5;

    private readonly AssetIndex _assets;
    private readonly float _brightness;
    private readonly Tree3DSettings _settings;
    private readonly TreeAlphaCoverage _alpha;
    private readonly Dictionary<(string Model, int Level, bool Native, string? Variant), Selection> _selections = [];
    private readonly ConcurrentDictionary<string, string> _fingerprints = new(StringComparer.Ordinal);
    public sealed record Selection(string? MeshPath, string Source, int OriginalTriangles, int Triangles, int Vertices, string Method, string? Reason,
        int FoliageTrianglesRemoved = 0, float FoliageCoverageLoss = 0);
    private readonly ConcurrentDictionary<string, Lazy<LodMesh?>> _raw = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, LodMesh?> _built = new(StringComparer.OrdinalIgnoreCase);

    public Tree3DSource(AssetIndex assets, float brightness = 1f, Tree3DSettings? settings = null)
    {
        _assets = assets;
        _alpha = new(assets);
        _brightness = brightness;
        _settings = settings ?? new Tree3DSettings(true); _settings.Validate();
    }

    public static string MeshPath(string modelPath) => Prefix + modelPath;
    private static string LevelPath(string model, int level, bool native, TreeMaterialVariant? variant) =>
        Prefix + level + "|" + (native ? "native|" : "lod|") + (variant is null ? "" : variant.Id + "|") + model;
    private static string SourcePath(string path) => path[(path.LastIndexOf('|') + 1)..];

    public bool Owns(string path) => path.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>
    /// Tree models are real models, so their textures go through the same mapping as any LOD mesh's. The one that matters here: a trunk shape has no
    /// alpha property, yet the engine's LOD shader alpha-tests it, and the bark textures of tree mods keep gloss in their alpha channel, so the trunks
    /// came out as strips with holes between them.
    /// </summary>
    public bool MapTextures(string path) => true;

    public string? Fingerprint(string path)
        => Owns(path) ? FormattableString.Invariant($"t{Version}|b{_brightness:F3}|{_settings.Fingerprint}|{_assets.Fingerprint(SourcePath(path))}")
            + _fingerprints.GetValueOrDefault(path, "") : null;

    public LodMesh? Build(string path) => _built.GetOrAdd(path, p => Process(p, Raw(SourcePath(p))));

    private LodMesh? Raw(string modelPath) => _raw.GetOrAdd(modelPath, p => new Lazy<LodMesh?>(() => Load(p))).Value;

    private LodMesh? Load(string modelPath)
    {
        if (!_assets.TryOpen(modelPath, out var s)) return null;
        byte[] bytes;
        using (s)
        {
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            bytes = ms.ToArray();
        }
        return NifGeometryReader.Read(modelPath, bytes, passthru: true);
    }

    /// <summary>
    /// Null when the model is fine to use, otherwise why not: unreadable, no shapes, or a shape whose diffuse texture
    /// doesn't exist (usually a trunk billboard TexGen hasn't made).
    /// </summary>
    public string? Validate(string modelPath, TreeMaterialVariant? variant = null, bool native = false)
    {
        if (variant is not null && !native) return "authored tree LOD has no verified mapping for this record's alternate textures";
        LodMesh? mesh;
        try { mesh = Raw(modelPath); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return "could not be read: " + ex.Message; }
        if (mesh is null) return "could not be found";
        if (mesh.Parts.Count == 0)
            return mesh.Warnings.Count > 0 ? "no usable shapes (" + string.Join("; ", mesh.Warnings.Distinct().Take(2)) + ")" : "no usable shapes";
        if (mesh.Warnings.Any(w => w.Contains("Skinned shape", StringComparison.Ordinal) || w.Contains("could not be read", StringComparison.Ordinal) || w.Contains("not supported", StringComparison.Ordinal)))
            return "unsupported geometry: " + string.Join("; ", mesh.Warnings.Take(3));
        if (!TreeMaterials.TryApply(mesh, variant, out mesh, out var reason)) return reason;
        foreach (var texture in variant?.Overrides.SelectMany(o => o.Slots).Where(s => !string.IsNullOrEmpty(s)) ?? [])
            if (!_assets.Exists(texture!)) return "missing alternate texture " + texture;
        foreach (var part in mesh.Parts)
        {
            if (part.Positions.Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z))
                || part.UVs.Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Y))) return "non-finite geometry or UV coordinates";
            var tex = part.Material.Textures.Count > 0 ? part.Material.Textures[0] : "";
            if (string.IsNullOrWhiteSpace(tex)) return "a shape has no diffuse texture";
            var path = tex.Replace('/', '\\').TrimStart('\\');
            if (!path.StartsWith("textures\\", StringComparison.OrdinalIgnoreCase)) path = "textures\\" + path;
            if (!_assets.Exists(path)) return "missing texture " + path;
        }
        return null;
    }

    /// <summary>Vertex and triangle counts of a usable model (0, 0 when it can't be read).</summary>
    public (int Vertices, int Triangles) Size(string modelPath)
    {
        LodMesh? mesh;
        try { mesh = Raw(modelPath); } catch (Exception e) when (e is not OperationCanceledException) { return (0, 0); }
        return mesh is null ? (0, 0) : (mesh.Parts.Sum(p => p.VertexCount), mesh.Parts.Sum(p => p.TriangleCount));
    }

    public Selection Select(string model, int level, bool native = false, TreeMaterialVariant? variant = null)
    {
        RunLimits.Check();
        var key = (Model: model.ToLowerInvariant(), Level: level, Native: native, Variant: variant?.Id);
        if (_selections.TryGetValue(key, out var saved)) return saved;
        string selected = model;
        bool distinct = false;
        if (variant is not null && !native)
        {
            if (!_settings.GenerateMissing)
                return Save(new(null, selected, 0, 4, 8, "Billboard", "Authored LOD has no verified mapping for this record's texture variant."));
            selected = variant.FullModel; native = true;
        }
        if (level == 8 && variant is null)
        {
            string? far = null;
            foreach (var marker in new[] { "_lod_0.nif", "_lod0.nif" })
                if (model.EndsWith(marker, StringComparison.OrdinalIgnoreCase)) far = model[..^marker.Length] + marker.Replace("0", "1");
            if (native) far = model[..^4] + "_lod_1.nif";
            if (far is not null && _assets.Exists(far) && Validate(far) is null) { selected = far; distinct = true; native = false; }
        }
        var invalid = Validate(selected, variant, native);
        if (invalid is not null) return Save(new(null, selected, 0, 4, 8, "Billboard", invalid));
        var raw = Raw(selected);
        if (raw is null) return Save(new(null, selected, 0, 0, 0, "Billboard", "Source model cannot be read."));
        if (!TreeMaterials.TryApply(raw, variant, out raw, out var variantError))
            return Save(new(null, selected, 0, 4, 8, "Billboard", variantError));
        if (native) raw = NativeMaterial(raw);
        int budget = level == 4 ? _settings.NearTriangleBudget : _settings.FarTriangleBudget;
        float error = level == 4 ? _settings.NearMaxError : _settings.FarMaxError;
        var near = level == 8 ? Select(model, 4, key.Native, variant) : null;
        int target = budget;
        if (near is { MeshPath: not null } && !distinct && _settings.ReduceFoliage)
        {
            int lowerCost = Math.Max(1, (int)(near.Triangles * .65f));
            target = budget > 0 ? Math.Min(budget, lowerCost) : lowerCost;
        }
        float? foliageLoss = _settings.ReduceFoliage ? level == 4 ? _settings.NearFoliageLoss : _settings.FarFoliageLoss : null;
        var reduced = TreeMeshReducer.Reduce(raw, target, error, foliageLoss, _alpha.Sampler);
        if (budget > 0 && reduced.Triangles > budget) return Save(new(null, selected, reduced.OriginalTriangles, 4, 8, "Billboard",
            $"{reduced.Triangles:N0} triangles exceed LOD{level} budget {budget:N0} within the coverage limit. {reduced.FoliageNote}"));
        // A farther level must earn its cost: if the same mesh cannot be reduced, use crossed cards.
        if (level == 8 && !distinct && reduced.Triangles >= raw.Parts.Sum(p => p.TriangleCount))
            return Save(new(null, selected, reduced.OriginalTriangles, 4, 8, "Billboard", "No simpler far model; retained intact foliage as a billboard fallback."));
        if (level == 8)
        {
            if (near!.MeshPath is null || reduced.Triangles > near.Triangles)
                return Save(new(null, selected, reduced.OriginalTriangles, 4, 8, "Billboard", "Near level uses billboards or the far model is heavier than the near model."));
        }
        string path = LevelPath(selected, level, native, variant);
        var processed = Process(path, reduced.Mesh)!; _built[path] = processed;
        _fingerprints[path] = "|variant:" + variant?.Id + "|textures:" + string.Join('|', raw.Parts
            .SelectMany(p => p.Material.Textures).Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).Select(_assets.Fingerprint));
        return Save(new(path, selected, reduced.OriginalTriangles, reduced.Triangles, processed.Parts.Sum(p => p.VertexCount),
            distinct ? "Authored far model" : native ? "Native source reduction" : reduced.Triangles < reduced.OriginalTriangles ? "Native reduction" : "Installed model", null,
            reduced.FoliageTrianglesRemoved, reduced.FoliageCoverageLoss));
        Selection Save(Selection value) { _selections[key] = value; return value; }
    }

    private static LodMesh NativeMaterial(LodMesh mesh) => new()
    {
        Path = mesh.Path, Warnings = mesh.Warnings, Parts = mesh.Parts.Select(p => new LodMeshPart
        {
            Name = p.Name, Positions = p.Positions,
            UVs = p.Material.Passthru is { } uv ? p.UVs.Select(v => v * new Vector2(uv.UvScaleX, uv.UvScaleY) + new Vector2(uv.UvOffsetX, uv.UvOffsetY)).ToArray() : p.UVs, Normals = p.Normals, Tangents = p.Tangents, Bitangents = p.Bitangents,
            Colors = p.Colors, Triangles = p.Triangles,
            Material = p.Material with { ShaderFlags1 = 0x80000300u | (p.Material.ShaderFlags1 & 0x1008u),
                ShaderFlags2 = 5u | (p.Material.ShaderFlags2 & 0x50u), Passthru = null }
        }).ToArray()
    };

    public IReadOnlyList<Tree3DLevelReport> Report(IEnumerable<TreeReference> trees, Tree3DSettings settings)
    {
        var reports = new List<Tree3DLevelReport>();
        foreach (var group in trees.GroupBy(t => (t.Model3D!, t.Model3DGenerated, t.MaterialVariant?.Id)))
            foreach (int level in settings.Lod8 ? new[] { 4, 8 } : new[] { 4 })
            {
                var s = Select(group.Key.Item1, level, group.Key.Model3DGenerated, group.First().MaterialVariant); int count = group.Count();
                reports.Add(new(group.Key.Item1, level, s.Source, count, s.OriginalTriangles, s.Triangles, (long)s.Triangles * count,
                    ((long)s.Vertices * 32 + (long)s.Triangles * 6) * count / 1048576.0, s.Method, s.Reason, group.Key.Id,
                    s.FoliageTrianglesRemoved, s.FoliageCoverageLoss));
            }
        return reports.OrderBy(r => r.Level).ThenByDescending(r => r.PlacedTriangles).ToArray();
    }

    /// <summary>Object LOD references for the trees: 3D model at LOD4 (and LOD8 if asked), billboard cards further out.</summary>
    public IEnumerable<LodReference> ToReferences(IEnumerable<TreeReference> trees, Tree3DSettings settings)
    {
        foreach (var t in trees)
        {
            var near = Select(t.Model3D!, 4, t.Model3DGenerated, t.MaterialVariant);
            var model = near.MeshPath ?? TreeCardSource.Prefix + t.Billboard.TexturePath;
            var card = TreeCardSource.Prefix + t.Billboard.TexturePath;
            var near8 = settings.Lod8 ? Select(t.Model3D!, 8, t.Model3DGenerated, t.MaterialVariant).MeshPath ?? card : card;
            // Trees copied in from a child worldspace (walled cities) only matter close up, as before.
            var meshes = t.ObjectLod ? new LodMeshSet(model, near8, null, null) : new LodMeshSet(model, near8, card, settings.MapTrees ? card : null);
            yield return new LodReference(
                FormKey: t.FormKey,
                BaseFormKey: t.BaseFormKey ?? "tree",
                BaseEditorId: "3D tree LOD",
                Worldspace: t.Worldspace,
                WinningPlugin: "tree LOD",
                Position: t.Position,
                RotationRadians: new Vector3(0, 0, t.RotationZ),
                Scale: t.Scale,
                Meshes: meshes,
                Flags: LodReferenceFlags.None);
        }
    }

    private LodMesh? Process(string path, LodMesh? raw)
    {
        if (raw is null || raw.Parts.Count == 0) return null;

        // The model's own centre, for spherenormals shapes.
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var part in raw.Parts)
            foreach (var p in part.Positions) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
        var center = (min + max) * 0.5f;

        bool dim = MathF.Abs(_brightness - 1f) > 0.001f;
        var parts = new List<LodMeshPart>(raw.Parts.Count);
        foreach (var part in raw.Parts)
        {
            var normals = part.Normals;
            if (part.Name?.Contains("spherenormals", StringComparison.OrdinalIgnoreCase) == true)
            {
                normals = new Vector3[part.VertexCount];
                for (int i = 0; i < normals.Length; i++)
                    normals[i] = Transforms.SafeNormalize(part.Positions[i] - center, part.Normals[i]);
            }

            // A tree model's vertex alpha is not opacity: the tree shader reads it as the wind weight of the vertex (0 at the trunk, 1 at the
            // tips; the pine crown of Happy Little Trees' passthru model has half its vertices under 0.5). The LOD shader multiplies it into the
            // alpha test instead, which cuts the inner foliage away and leaves a thin, bald crown. Every vertex goes in fully opaque.
            var colors = part.Colors;
            if (colors is not null && colors.Any(c => (c >> 24) != 0xFFu))
            {
                colors = new uint[part.VertexCount];
                for (int i = 0; i < colors.Length; i++) colors[i] = part.Colors![i] | 0xFF00_0000u;
            }
            if (dim)
            {
                var src = colors;
                colors = new uint[part.VertexCount];
                for (int i = 0; i < colors.Length; i++) colors[i] = BtoBuilder.Scale(src?[i] ?? 0xFFFF_FFFFu, _brightness);
            }

            parts.Add(new LodMeshPart
            {
                Name = part.Name,
                Material = part.Material,
                Positions = part.Positions, UVs = part.UVs, Normals = normals,
                Tangents = part.Tangents, Bitangents = part.Bitangents,
                Colors = colors, Triangles = part.Triangles,
            });
        }
        return new LodMesh { Path = path, Parts = parts, Warnings = raw.Warnings };
    }
}
