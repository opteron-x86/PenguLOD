using System.Numerics;
using System.Text;
using AnvilLOD.Core.World;
using AnvilLOD.Meshes;
using AnvilLOD.Meshes.Authoring;
using AnvilLOD.Meshes.Nif;
using AnvilLOD.Plugins;

namespace AnvilLOD.Core.Tests;

public sealed class TexturePathTests : IDisposable
{
    private const string AuthorPath = @"d:\modding\skyrim - the fatherland\mods\skyland aio\textures\architecture\riften\riftensiding01.dds";
    private const string GameTexture = @"textures\architecture\riften\riftensiding01.dds";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "anvillod-texture-path-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Theory]
    [InlineData(AuthorPath, GameTexture)]
    [InlineData(@"textures\" + AuthorPath, GameTexture)]
    [InlineData("D:/Modding/Data/Textures/architecture/riften/a.dds", @"Textures\architecture\riften\a.dds")]
    [InlineData(@"\\author\mods\skyland\textures\a.dds", @"textures\a.dds")]
    [InlineData(@"Data\Textures\a.dds", @"Textures\a.dds")]
    [InlineData("architecture/riften/a.dds", @"textures\architecture\riften\a.dds")]
    [InlineData(@"textures\mytextures\textures\a.dds", @"textures\mytextures\textures\a.dds")]
    [InlineData("", "")]
    public void Texture_references_use_the_game_texture_root(string source, string expected)
        => Assert.Equal(expected, GamePath.NormalizeTexture(source));

    [Theory]
    [InlineData(@"textures\d:\missing.dds")]
    [InlineData(@"D:\missing.dds")]
    [InlineData(@"textures\..\outside.dds")]
    [InlineData("/tmp/outside.dds")]
    public void Filesystem_boundary_rejects_unsafe_game_paths_on_every_OS(string path)
    {
        Assert.Throws<ArgumentException>(() => GamePath.ToFileSystemPath(_root, path));
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public void Missing_texture_stub_cannot_create_a_drive_named_directory()
    {
        StubTextures.Write(_root, @"textures\" + AuthorPath);
        Assert.True(File.Exists(GamePath.ToFileSystemPath(_root, GameTexture)));
        Assert.DoesNotContain(Directory.EnumerateFileSystemEntries(_root, "*", SearchOption.AllDirectories),
            p => Path.GetFileName(p).Contains(':') || Path.GetFileName(p).Contains('\\'));
        Assert.Throws<ArgumentException>(() => StubTextures.Write(_root, @"d:\unrecoverable.dds"));
        Assert.Throws<ArgumentException>(() => StubTextures.Write(_root, @"textures\..\escape.dds"));
    }

    [Fact]
    public void Authored_NIF_paths_resolve_existing_textures_and_are_correct_in_generated_bytes()
    {
        // Build a valid NIF, then insert the raw author's path without passing through our writer's normalizer.
        var placeholder = "textures\\" + new string('x', AuthorPath.Length - 13) + ".dds";
        var part = Triangle(placeholder);
        var source = LodNifWriter.Write("Riften fixture", [part]);
        int offset = source.AsSpan().IndexOf(Encoding.ASCII.GetBytes(placeholder));
        Assert.True(offset >= 0);
        Encoding.ASCII.GetBytes(AuthorPath).CopyTo(source, offset);

        var read = NifGeometryReader.Read("Riften fixture", source);
        Assert.Equal(GameTexture, Assert.Single(read.Parts).Material.Textures[0]);
        Assert.Empty(LodGenerator.FindMissingTextures([read], p => p == GameTexture));

        // Read the raw texture block so the reader's normalization cannot hide a writer regression.
        var written = NifFile.Read(LodNifWriter.Write("Riften repaired", read.Parts));
        var block = Assert.Single(written.Blocks, b => b.Type == "BSShaderTextureSet");
        var slots = written.ReaderFor(block);
        Assert.Equal(9u, slots.U32());
        Assert.Equal(GameTexture, slots.SizedString());
        Assert.DoesNotContain(@"d:\", Encoding.ASCII.GetString(written.Data), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(GameTexture, ShaderGuard.BakedSlots([AuthorPath])[0]);
    }

    private static LodMeshPart Triangle(string texture) => new()
    {
        Material = new([texture], 0x80000300, 5, 3, false, 0, 0, Vector3.Zero, 1),
        Positions = [Vector3.Zero, Vector3.UnitX, Vector3.UnitY],
        UVs = [Vector2.Zero, Vector2.UnitX, Vector2.UnitY],
        Normals = [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
        Tangents = [Vector3.UnitX, Vector3.UnitX, Vector3.UnitX],
        Bitangents = [Vector3.UnitY, Vector3.UnitY, Vector3.UnitY],
        Triangles = [0, 1, 2],
    };
}
