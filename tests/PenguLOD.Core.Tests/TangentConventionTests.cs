using System.Numerics;
using Mutagen.Bethesda;
using PenguLOD.Meshes;
using PenguLOD.Meshes.Authoring;
using PenguLOD.Meshes.Nif;
using PenguLOD.Plugins;
using PenguLOD.Plugins.Authoring;
using PenguLOD.Testing;

namespace PenguLOD.Core.Tests;

/// <summary>
/// Bethesda meshes store the tangent frame with the NIF "tangent" along +V and "bitangent" along +U (nifly's
/// CalcTangentSpace, and measured on shipped SSE meshes). Generated frames and the renderers must agree with that.
/// </summary>
public class TangentConventionTests
{
    /// <summary>nifly's BSTriShape::CalcTangentSpace, written out independently as the reference.</summary>
    private static (Vector3[] Tangents, Vector3[] Bitangents) Nifly(Vector3[] pos, Vector2[] uv, Vector3[] nrm, ushort[] tris)
    {
        var tan1 = new Vector3[pos.Length]; var tan2 = new Vector3[pos.Length];
        for (int t = 0; t < tris.Length; t += 3)
        {
            int i1 = tris[t], i2 = tris[t + 1], i3 = tris[t + 2];
            var v1 = pos[i1]; var v2 = pos[i2]; var v3 = pos[i3];
            var w1 = uv[i1]; var w2 = uv[i2]; var w3 = uv[i3];
            float x1 = v2.X - v1.X, x2 = v3.X - v1.X, y1 = v2.Y - v1.Y, y2 = v3.Y - v1.Y, z1 = v2.Z - v1.Z, z2 = v3.Z - v1.Z;
            float s1 = w2.X - w1.X, s2 = w3.X - w1.X, t1 = w2.Y - w1.Y, t2 = w3.Y - w1.Y;
            float r = s1 * t2 - s2 * t1; r = r >= 0 ? 1 : -1;
            var sdir = Vector3.Normalize(new Vector3((t2 * x1 - t1 * x2) * r, (t2 * y1 - t1 * y2) * r, (t2 * z1 - t1 * z2) * r));
            var tdir = Vector3.Normalize(new Vector3((s1 * x2 - s2 * x1) * r, (s1 * y2 - s2 * y1) * r, (s1 * z2 - s2 * z1) * r));
            foreach (int i in new[] { i1, i2, i3 }) { tan1[i] += tdir; tan2[i] += sdir; }
        }
        var tangents = new Vector3[pos.Length]; var bitangents = new Vector3[pos.Length];
        for (int i = 0; i < pos.Length; i++)
        {
            var n = nrm[i];
            var t = Vector3.Normalize(tan1[i] - n * Vector3.Dot(n, tan1[i]));
            var b = tan2[i] - n * Vector3.Dot(n, tan2[i]);
            b = Vector3.Normalize(b - t * Vector3.Dot(t, b));
            tangents[i] = t; bitangents[i] = b;
        }
        return (tangents, bitangents);
    }

    private static void Near(Vector3 expected, Vector3 actual, float tolerance = 1e-3f) =>
        Assert.True(Vector3.Distance(expected, actual) < tolerance, $"expected {expected}, got {actual}");

    [Fact]
    public void GeneratedTangentsMatchNiflyOnAShearedBentStrip()
    {
        // Three quads bent along their length, with U and V neither orthogonal nor aligned to the axes.
        var pos = new List<Vector3>(); var uv = new List<Vector2>(); var tris = new List<ushort>();
        for (int i = 0; i <= 3; i++)
        {
            float x = i * 100, z = i * i * 15;
            pos.Add(new(x, 0, z)); pos.Add(new(x + 30, 80, z + 10));
            uv.Add(new(i * .3f, 1)); uv.Add(new(i * .3f + .1f, .2f));
        }
        for (int i = 0; i < 3; i++)
        {
            ushort a = (ushort)(i * 2), b = (ushort)(a + 1), c = (ushort)(a + 2), d = (ushort)(a + 3);
            tris.AddRange([a, c, b, b, c, d]);
        }
        var p = pos.ToArray(); var u = uv.ToArray(); var t = tris.ToArray();
        var n = new Vector3[p.Length];
        for (int k = 0; k < t.Length; k += 3)
        {
            var fn = Vector3.Cross(p[t[k + 1]] - p[t[k]], p[t[k + 2]] - p[t[k]]);
            n[t[k]] += fn; n[t[k + 1]] += fn; n[t[k + 2]] += fn;
        }
        for (int k = 0; k < n.Length; k++) n[k] = Vector3.Normalize(n[k]);

        var (tangents, bitangents) = NifGeometryReader.TangentsFromUv(p, u, n, t);
        var reference = Nifly(p, u, n, t);
        for (int i = 0; i < p.Length; i++) { Near(reference.Tangents[i], tangents[i]); Near(reference.Bitangents[i], bitangents[i]); }
        // Distinct from the textbook frame: the tangent field must not follow U.
        var dPdu = Vector3.Normalize(p[2] - p[0]);
        Assert.True(MathF.Abs(Vector3.Dot(tangents[0], dPdu)) < .9f);
    }

    [Fact]
    public void LegacyShapesWithoutTangentsGetTheNifFrame()
    {
        // The fixture maps u = x / 400 and v = 1 - z / 400: +U is +X and +V is -Z.
        var part = Assert.Single(NifGeometryReader.Read("le", ModelNifFixture.Create(ModelNifFixture.Geometry.Triangles)).Parts);
        Near(-Vector3.UnitZ, part.Tangents[0], .02f);
        Near(Vector3.UnitX, part.Bitangents[0], .02f);
    }

    [Fact]
    public void SynthesisedCardsCarryTheFrameTheirUvsImply()
    {
        using var f = new TextureFixture();
        const string image = @"textures\terrain\lodgen\plain.dds";
        TextureFixture.Put(f.Data, image, TextureFixture.Solid(12, 200, 18));
        TextureFixture.Put(f.Data, @"textures\terrain\lodgen\plain.txt", System.Text.Encoding.UTF8.GetBytes("Width=100\nHeight=200\nShiftZ=0\n"));
        var cards = new TreeCardSource(AssetIndex.Build(GameRelease.SkyrimSE, f.Data)).Build(TreeCardSource.Prefix + image)!;
        var entry = new HybridTreeAuthor.Entry("textures\\pengulod\\hybrid\\aa\\x", new(100, 300, 0), new(80, 300, 0), new Dictionary<string, string>());
        foreach (var part in cards.Parts.Append(HybridTreeAuthor.Card(entry, false)).Append(HybridTreeAuthor.Card(entry, true)))
        {
            var (tangents, bitangents) = NifGeometryReader.TangentsFromUv(part.Positions, part.UVs, part.Normals, part.Triangles);
            for (int i = 0; i < part.VertexCount; i++) { Near(tangents[i], part.Tangents[i]); Near(bitangents[i], part.Bitangents[i]); }
        }
    }

    [Fact]
    public void BillboardRedFollowsUAndGreenFollowsV()
    {
        // A card facing -Y with +U along +X and +V down (-Z), in the NIF frame.
        var card = TextureFixture.Card("card", "map") with { Material = TextureFixture.Card("card", "map").Material with { Textures = ["diffuse", "normal", "", "", "", "", "", "", ""] } };
        var mesh = new LodMesh { Path = "card", Parts = [card] };
        BillboardImage Render(byte r, byte g) => BillboardRenderer.Render(mesh,
            path => path == "normal" ? (new byte[] { r, g, 128, 255 }, 1, 1) : (new byte[] { 200, 200, 200, 255 }, 1, 1), 32, shaded: false)!;
        int center = (16 * 32 + 16) * 4;
        var red = Render(255, 128).NormalRgba!; var green = Render(128, 255).NormalRgba!;
        // Output: R = 128 + 127 x, G = 128 - 127 z. Red tilts the normal towards +X; green towards +V, which is -Z.
        Assert.True(red[center] > 200, $"red map: R={red[center]}"); Assert.InRange(red[center + 1], 120, 136);
        Assert.True(green[center + 1] > 200, $"green map: G={green[center + 1]}"); Assert.InRange(green[center], 120, 136);
    }

    [Fact]
    public void SimplifiedPartsKeepTheirShapeNamesInWrittenLod()
    {
        // A dense grid (more than four triangles) goes through the simplifier; its name carries LODGen instructions.
        var pos = new List<Vector3>(); var uv = new List<Vector2>(); var tris = new List<ushort>();
        for (int y = 0; y <= 6; y++)
            for (int x = 0; x <= 6; x++) { pos.Add(new(x * 100, 0, y * 100)); uv.Add(new(x / 6f, 1 - y / 6f)); }
        for (int y = 0; y < 6; y++)
            for (int x = 0; x < 6; x++)
            {
                ushort a = (ushort)(y * 7 + x), b = (ushort)(a + 1), c = (ushort)(a + 7), d = (ushort)(a + 8);
                tris.AddRange([a, b, c, b, d, c]);
            }
        int n = pos.Count;
        var part = TextureFixture.Card("WallTrim", @"textures\red.dds") with
        {
            Tier = 2, TexGenScale = 1.5f, Positions = [.. pos], UVs = [.. uv],
            Normals = Enumerable.Repeat(-Vector3.UnitY, n).ToArray(), Tangents = Enumerable.Repeat(-Vector3.UnitZ, n).ToArray(),
            Bitangents = Enumerable.Repeat(Vector3.UnitX, n).ToArray(), Triangles = [.. tris],
        };
        var simplified = MeshSimplifier.Simplify(part, 50f);
        Assert.True(simplified.TriangleCount < part.TriangleCount);
        Assert.Equal(("WallTrim", 2, 1.5f), (simplified.Name, simplified.Tier, simplified.TexGenScale));

        var level = LodMeshAuthor.Build(new LodMesh { Path = "wall.nif", Parts = [part] }).First();
        var written = NifGeometryReader.Read("wall_lod_0.nif", LodNifWriter.Write("wall_lod_0", level.Parts, preserveShapeNames: true));
        Assert.Equal("WallTrim", Assert.Single(written.Parts).Name);
    }
}
