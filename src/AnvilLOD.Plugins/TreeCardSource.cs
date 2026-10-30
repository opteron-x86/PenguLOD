using System.Collections.Concurrent;
using System.Numerics;
using AnvilLOD.Core.World;
using AnvilLOD.Meshes;

namespace AnvilLOD.Plugins;

/// <summary>
/// Tree LOD for trees placed by light (ESL-flagged) plugins. The engine never hides <c>.btt</c> tree LOD for
/// those references (their FE-prefixed FormIDs don't match), so the LOD tree stays standing next to the real one.
/// Instead they go into the object LOD blocks as two crossed billboard cards, which the engine hides per cell
/// like any other object LOD.
/// </summary>
public sealed class TreeCardSource : ISyntheticMeshSource
{
    public const string Prefix = "~anvillod-treecard|";
    private const int Version = 2;

    private readonly AssetIndex _assets;
    private readonly float _brightness;
    private readonly ConcurrentDictionary<string, LodMesh?> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="brightness">Tree LOD brightness (1 = as the billboard is). Applied through vertex colours.</param>
    public TreeCardSource(AssetIndex assets, float brightness = 1f)
    {
        _assets = assets;
        _brightness = brightness;
    }

    public static bool NeedsCards(TreeReference t) => t.ObjectLod || (t.RuntimeFormId >> 24) == 0xFE;

    /// <summary>LOD references for the given trees: LOD4 and LOD8 blocks, z-rotation and scale from the tree.</summary>
    public IEnumerable<LodReference> ToReferences(IEnumerable<TreeReference> trees)
    {
        foreach (var t in trees)
        {
            var mesh = Prefix + t.Billboard.TexturePath;
            yield return new LodReference(
                FormKey: t.FormKey,
                BaseFormKey: t.BaseFormKey ?? "tree",
                BaseEditorId: "Tree LOD card",
                Worldspace: t.Worldspace,
                WinningPlugin: "tree LOD",
                Position: t.Position,
                RotationRadians: new Vector3(0, 0, t.RotationZ),
                Scale: t.Scale,
                Meshes: new LodMeshSet(mesh, mesh, null, null),
                Flags: LodReferenceFlags.None);
        }
    }

    public bool Owns(string path) => path.StartsWith(Prefix, StringComparison.Ordinal);

    public string? Fingerprint(string path)
    {
        if (!Owns(path)) return null;
        var (dds, normal, txt) = Files(path[Prefix.Length..]);
        var side = txt[..^4] + "_2";
        return $"c{Version}|b{_brightness:F3}|{_assets.Fingerprint(dds)}|{_assets.Fingerprint(normal)}|{_assets.Fingerprint(txt)}|{_assets.Fingerprint(side + ".dds")}|{_assets.Fingerprint(side + "_n.dds")}";
    }

    public LodMesh? Build(string path) => _cache.GetOrAdd(path, p => Create(p[Prefix.Length..]));

    /// <summary>Prefers the front view (<c>_1.dds</c> + <c>_1_n.dds</c>), which TexGen renders with a normal map.</summary>
    private (string Dds, string Normal, string Txt) Files(string plainDds)
    {
        var stem = plainDds.EndsWith("_1.dds", StringComparison.OrdinalIgnoreCase) ? plainDds[..^6] : plainDds[..^4];
        return _assets.Exists(stem + "_1.dds") ? (stem + "_1.dds", stem + "_1_n.dds", stem + ".txt")
            : (stem + ".dds", stem + "_n.dds", stem + ".txt");
    }

    private LodMesh? Create(string plainDds)
    {
        var (dds, normal, txt) = Files(plainDds);
        if (!_assets.Exists(dds) || !_assets.TryOpen(txt, out var st)) return null;
        string text;
        using (var reader = new StreamReader(st)) text = reader.ReadToEnd();
        var bb = TreeBillboard.Parse(dds, text, useFirstView: dds.EndsWith("_1.dds", StringComparison.OrdinalIgnoreCase));
        if (bb is null) return null;

        string sideDds = txt[..^4] + "_2.dds", sideNormal = txt[..^4] + "_2_n.dds";
        if (_assets.Exists(sideDds) && TreeBillboard.Parse(sideDds, text, view: 2) is { } side)
            return new LodMesh { Path = Prefix + plainDds, Parts = [ViewCard(bb, normal, false), ViewCard(side, sideNormal, true)] };

        var material = new LodMaterial(
            [dds, _assets.Exists(normal) ? normal : "", "", "", "", "", "", "", ""],
            0x8000_0300u,
            0x0000_0005u | 0x10u, // double-sided
            3,
            true, GrassPatchBuilder.AlphaTestFlags, GrassPatchBuilder.AlphaThreshold,
            Vector3.Zero, 1f);

        // Two upright cards crossing at the trunk, bottom at ShiftZ (scaled with the tree by the reference transform).
        float hw = bb.Width * 0.5f, z0 = bb.ShiftZ, z1 = bb.ShiftZ + bb.Height;
        var pos = new Vector3[8];
        var uv = new Vector2[8];
        var nrm = new Vector3[8];
        var tan = new Vector3[8];
        var bit = new Vector3[8];
        var tris = new ushort[12];
        for (int c = 0; c < 2; c++)
        {
            var dir = c == 0 ? Vector3.UnitX : Vector3.UnitY;
            var normalDir = c == 0 ? -Vector3.UnitY : Vector3.UnitX;
            int v = c * 4;
            pos[v] = -dir * hw + new Vector3(0, 0, z0);
            pos[v + 1] = dir * hw + new Vector3(0, 0, z0);
            pos[v + 2] = dir * hw + new Vector3(0, 0, z1);
            pos[v + 3] = -dir * hw + new Vector3(0, 0, z1);
            uv[v] = new Vector2(0, 1); uv[v + 1] = new Vector2(1, 1); uv[v + 2] = new Vector2(1, 0); uv[v + 3] = new Vector2(0, 0);
            for (int k = 0; k < 4; k++) { nrm[v + k] = normalDir; tan[v + k] = dir; bit[v + k] = Vector3.UnitZ; }
            int t = c * 6;
            tris[t] = (ushort)v; tris[t + 1] = (ushort)(v + 1); tris[t + 2] = (ushort)(v + 2);
            tris[t + 3] = (ushort)v; tris[t + 4] = (ushort)(v + 2); tris[t + 5] = (ushort)(v + 3);
        }
        uint[]? colors = null;
        if (MathF.Abs(_brightness - 1f) > 0.001f)
        {
            var c = BtoBuilder.Scale(0xFFFF_FFFFu, _brightness);
            colors = new uint[pos.Length];
            Array.Fill(colors, c);
        }
        return new LodMesh
        {
            Path = Prefix + plainDds,
            Parts = [new LodMeshPart { Material = material, Positions = pos, UVs = uv, Normals = nrm, Tangents = tan, Bitangents = bit, Colors = colors, Triangles = tris }],
        };
    }

    private LodMeshPart ViewCard(TreeBillboard bb, string normal, bool side)
    {
        var dir = side ? Vector3.UnitY : Vector3.UnitX;
        var normalDir = side ? Vector3.UnitX : -Vector3.UnitY;
        float half = bb.Width * .5f;
        var bottom = new Vector3(0, 0, bb.ShiftZ); var top = bottom + Vector3.UnitZ * bb.Height;
        return new LodMeshPart
        {
            Name = side ? "Side billboard" : "Front billboard",
            Material = new LodMaterial([bb.TexturePath, _assets.Exists(normal) ? normal : "", "", "", "", "", "", "", ""],
                0x8000_0300u, 0x0000_0015u, 3, true, GrassPatchBuilder.AlphaTestFlags, GrassPatchBuilder.AlphaThreshold, Vector3.Zero, 1),
            Positions = [bottom - dir * half, bottom + dir * half, top + dir * half, top - dir * half],
            UVs = [new(0, 1), new(1, 1), new(1, 0), new(0, 0)],
            Normals = [normalDir, normalDir, normalDir, normalDir], Tangents = [dir, dir, dir, dir],
            Bitangents = [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ], Triangles = [0, 1, 2, 0, 2, 3],
            Colors = MathF.Abs(_brightness - 1) <= .001f ? null : Enumerable.Repeat(BtoBuilder.Scale(0xFFFFFFFF, _brightness), 4).ToArray()
        };
    }
}

/// <summary>Several synthetic mesh sources behind one interface.</summary>
public sealed class CompositeSyntheticSource(params ISyntheticMeshSource?[] sources) : ISyntheticMeshSource
{
    private readonly ISyntheticMeshSource[] _sources = sources.Where(s => s is not null).ToArray()!;

    public bool Owns(string path) => _sources.Any(s => s.Owns(path));

    public LodMesh? Build(string path) => _sources.FirstOrDefault(s => s.Owns(path))?.Build(path);

    public bool MapTextures(string path) => _sources.FirstOrDefault(s => s.Owns(path))?.MapTextures(path) ?? false;
}
