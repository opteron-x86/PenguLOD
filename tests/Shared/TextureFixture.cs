using System.Numerics;
using AnvilLOD.Core.World;
using AnvilLOD.Meshes;
using AnvilLOD.Meshes.Authoring;
using AnvilLOD.Plugins;
using AnvilLOD.Textures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AnvilLOD.Testing;

public sealed class TextureFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "anvillod-textures-" + Guid.NewGuid().ToString("N"));
    public string Data => Path.Combine(Root, "Game", "Data");
    public string Output => Path.Combine(Root, "Textures");
    public string Resources => Path.Combine(Root, "DynDOLOD");
    public string Plugins => Path.Combine(Root, "plugins.txt");
    public GameContextOptions Game => new(DataFolder: Data, PluginsTxt: Plugins);
    public TextureGenerationRequest Request => new(Game, Output, Resources, Trees: false, Grass: false, BaseSize: 64, BillboardMaxSize: 64);
    public string RuleFile => Path.Combine(Resources, "Configs", "DynDOLOD_SSE_TexGen_noalpha_skyrimesm.txt");
    public TextureFixture()
    {
        Directory.CreateDirectory(Data); Directory.CreateDirectory(Path.Combine(Resources, "Configs"));
        var mod = new SkyrimMod(ModKey.FromNameAndExtension("Skyrim.esm"), SkyrimRelease.SkyrimSE);
        var tree = mod.Trees.AddNew(); tree.EditorID = "TestTree"; tree.Model = new Model { File = @"trees\testtree.nif" };
        mod.WriteToBinary(Path.Combine(Data, "Skyrim.esm"));
        File.WriteAllText(Plugins, "");
        Put(Data, @"textures\red.dds", Solid(240, 12, 6));
        Put(Data, @"textures\green.dds", Solid(8, 235, 10));
        Put(Data, @"textures\red_n.dds", Solid(128, 128, 255));
        Put(Data, @"textures\green_n.dds", Solid(128, 128, 255));
        Put(Data, @"textures\lod\object.dds", Solid(240, 12, 6));
        Put(Data, @"textures\lod\object_n.dds", Solid(128, 128, 255));
        File.WriteAllText(RuleFile, Tile(@"textures\red.dds", @"textures\lod\atlas.dds", 0) + "\n" + Tile(@"textures\green.dds", @"textures\lod\atlas.dds", 1));
        Put(Data, @"meshes\trees\testtree.nif", LodNifWriter.Write("tree", [Card("crown", @"textures\green.dds")]));
        Put(Resources, @"render\skyrim\objects\lod\object.nif", LodNifWriter.Write("TexGen Plane", [Card("TexGen Plane", @"textures\red.dds"), Card("object", @"textures\green.dds")]));
    }
    public static string Tile(string source, string output, float x) => FormattableString.Invariant($"0\t1\t1\t1\t{source}\t1\t1\t{x}\t0\t{output}\t2\t1");
    public static byte[] Solid(byte r, byte g, byte b, byte a = 255)
    {
        var pixels = new byte[8 * 8 * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = r; pixels[i + 1] = g; pixels[i + 2] = b; pixels[i + 3] = a; }
        return TextureImages.Encode(new(pixels, 8, 8));
    }
    public static LodMeshPart Card(string name, string texture) => new()
    {
        Name = name, Material = new LodMaterial([texture, "", "", "", "", "", "", "", ""], 0, 0, 3, false, 0, 0, Vector3.Zero, 1),
        Positions = [new(0, 0, 0), new(400, 0, 0), new(0, 0, 400), new(400, 0, 400)],
        UVs = [new(0, 1), new(1, 1), new(0, 0), new(1, 0)], Normals = [-Vector3.UnitY, -Vector3.UnitY, -Vector3.UnitY, -Vector3.UnitY],
        Tangents = [Vector3.UnitX, Vector3.UnitX, Vector3.UnitX, Vector3.UnitX], Bitangents = [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
        Triangles = [0, 1, 2, 1, 3, 2]
    };
    public static void Put(string root, string relative, byte[] bytes)
    {
        var path = GamePath.ToFileSystemPath(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes);
    }
    public void Dispose() => Directory.Delete(Root, true);
}
