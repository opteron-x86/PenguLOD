using AnvilLOD.Core.Lod;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.World;
using AnvilLOD.Plugins;
using AnvilLOD.Plugins.Authoring;
using AnvilLOD.Plugins.Mo2;
using AnvilLOD.Textures;
using AnvilLOD.Textures.Bc;
using AnvilLOD.Textures.Dds;
using Mutagen.Bethesda;

namespace AnvilLOD.Core.Tests;

public sealed class LinuxPathTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "anvillod-linux-" + Guid.NewGuid().ToString("N"));
    public LinuxPathTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Write(string text, params string[] parts)
    {
        var path = Path.Combine([_root, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    [Theory]
    [InlineData(@"Meshes\Terrain\Tamriel\Tree.NIF")]
    [InlineData("Meshes/Terrain/Tamriel/Tree.NIF")]
    [InlineData(@"Meshes/Terrain\Tamriel/Tree.NIF")]
    public void Game_path_names_are_independent_of_the_host_separator(string path)
    {
        Assert.Equal("Tree.NIF", GamePath.GetFileName(path));
        Assert.Equal("Tree", GamePath.GetFileNameWithoutExtension(path));
        Assert.Equal(Path.Combine(_root, "Meshes", "Terrain", "Tamriel", "Tree.NIF"), GamePath.ToFileSystemPath(_root, path));
    }

    [Fact]
    public void Case_insensitive_lookup_keeps_the_actual_host_path()
    {
        var actual = Write("mesh", "MixedCaseMod", "Meshes", "Landscape", "Rock.NIF");
        Assert.Equal(actual, GamePath.ResolveExisting(_root, @"mixedcasemod\meshes\landscape\rock.nif"), ignoreCase: OperatingSystem.IsWindows());
        Assert.Null(GamePath.ResolveExisting(_root, @"MixedCaseMod\Meshes\missing.nif"));
        Assert.Throws<ArgumentException>(() => GamePath.ResolveExisting(_root, @"..\outside.nif"));
    }

    [Fact]
    public void Batch_lookup_enumerates_mixed_case_directories_only_once()
    {
        if (OperatingSystem.IsWindows()) return;
        var expected = Enumerable.Range(0, 512).Select(i => Write("cache", "Grass", $"Tamrielx{i:0000}y0000.cgid")).ToArray();
        var enumerated = new Dictionary<string, int>();
        IEnumerable<string> Enumerate(string directory)
        {
            enumerated[directory] = enumerated.GetValueOrDefault(directory) + 1;
            return Directory.EnumerateFileSystemEntries(directory);
        }
        var lookup = new GamePathLookup(_root, Enumerate);
        foreach (var path in expected)
            Assert.Equal(path, lookup.ResolveExisting(GamePath.Normalize(Path.GetRelativePath(_root, path))));
        Assert.Null(lookup.ResolveExisting(@"grass\missing.cgid"));
        Assert.Equal(2, enumerated.Count);
        Assert.All(enumerated.Values, count => Assert.Equal(1, count));

        var assets = AssetIndex.Create(GameRelease.SkyrimSE, [], _ => [], _ => null);
        assets.SetOverrides(_root, expected.Select(p => GamePath.Normalize(Path.GetRelativePath(_root, p))), "grass\\");
        Assert.Equal(expected.Length, assets.EnumeratePaths("grass\\").Count());
        Assert.True(assets.TryOpen(@"grass\tamrielx0511y0000.cgid", out var stream));
        using (stream)
        using (var reader = new StreamReader(stream)) Assert.Equal("cache", reader.ReadToEnd());
    }

    [Fact]
    public void Batch_lookup_preserves_exact_matches_and_rejects_ambiguity_and_escaping_paths()
    {
        if (OperatingSystem.IsWindows()) return;
        var exact = Write("first", "Meshes", "Tree.NIF");
        Write("second", "Meshes", "TREE.nif");
        var lookup = new GamePathLookup(_root);
        Assert.Throws<IOException>(() => lookup.ResolveExisting(@"meshes\tree.nif"));
        Assert.Equal(exact, lookup.ResolveExisting(@"Meshes\Tree.NIF"));
        Assert.Throws<ArgumentException>(() => lookup.ResolveExisting(@"..\outside.nif"));
        Assert.Throws<ArgumentException>(() => lookup.ResolveExisting(@"C:\outside.nif"));
    }

    [Fact]
    public void Batch_lookup_can_cancel_during_directory_indexing()
    {
        Write("cache", "Grass", "Tamrielx0000y0000.cgid");
        using var cancellation = new CancellationTokenSource();
        IEnumerable<string> Enumerate(string directory)
        {
            cancellation.Cancel();
            return Directory.EnumerateFileSystemEntries(directory);
        }
        var lookup = new GamePathLookup(_root, Enumerate);
        Assert.Throws<OperationCanceledException>(() => lookup.ResolveExisting(@"missing\file.cgid", cancellation.Token));
    }

    [Fact]
    public void MO2_keeps_mod_priority_with_mixed_case_roots_and_unindexed_files()
    {
        Write("master", "Game", "dAtA", "SKYRIM.esm");
        Write("+High\n+Low\n", "Profiles", "Default", "modlist.txt");
        Write("", "Profiles", "Default", "plugins.txt");
        Write("low", "Mods", "Low", "Meshes", "Architecture", "Wall.NIF");
        var high = Write("high", "Mods", "High", "MESHES", "Architecture", "WALL.nif");
        Write("hidden", "Mods", "High", "MESHES", "Architecture", "Hidden.nif.mohidden");
        var billboard = Write("billboard", "Mods", "High", "Textures", "Terrain", "LODGen", "Tree.DDS");
        Write("low config", "Mods", "Low", "SKSE", "Plugins", "Custom.INI");
        var config = Write("high config", "Mods", "High", "SKSE", "Plugins", "Custom.INI");
        var instance = Mo2Instance.Open(_root, new Mo2Locations(Path.Combine(_root, "Game"),
            Path.Combine(_root, "Mods"), Path.Combine(_root, "Profiles")));
        var index = VirtualDataIndex.Build(instance.OpenProfile("Default"), ["meshes", @"textures\terrain\lodgen"]);

        Assert.Equal(high, index.ResolveLoose(@"meshes\architecture\wall.nif"), ignoreCase: OperatingSystem.IsWindows());
        Assert.Equal(billboard, index.ResolveLoose(@"textures\terrain\lodgen\tree.dds"), ignoreCase: OperatingSystem.IsWindows());
        Assert.Equal(config, index.FindLoose(@"skse\plugins\custom.ini"), ignoreCase: OperatingSystem.IsWindows());
        Assert.Null(index.FindLoose(@"meshes\architecture\hidden.nif"));
        Assert.Equal(2, index.LooseFileCount);
        Assert.Equal(0, index.MissingModFolders);
    }

    [Fact]
    public void Generated_asset_fallback_uses_case_insensitive_lookup()
    {
        Write("billboard", "Textures", "Terrain", "LODGen", "Tree.DDS");
        var assets = AssetIndex.Create(GameRelease.SkyrimSE, [], _ => [], _ => null);
        assets.FallbackRoot = _root;
        Assert.True(assets.TryOpen(@"textures\terrain\lodgen\tree.dds", out var stream));
        using (stream)
        using (var reader = new StreamReader(stream)) Assert.Equal("billboard", reader.ReadToEnd());
    }

    [Fact]
    public void Mesh_collection_accepts_uppercase_extensions_and_excludes_nested_output()
    {
        var model = Write("model", "Mod", "Meshes", "Architecture", "Wall.NIF");
        Write("old output", "Mod", "Meshes", "Generated", "AlreadyMade.NIF");
        var collected = LodMeshMaker.Collect([Path.Combine(_root, "Mod")], Path.Combine(_root, "Mod", "Meshes", "Generated"));
        Assert.Equal(model, Assert.Single(collected).File, ignoreCase: OperatingSystem.IsWindows());
        Assert.Equal(@"meshes\architecture\wall.nif", collected[0].Relative);
    }

    private sealed class TextureSource : IAssetSource
    {
        private readonly byte[] _dds;
        public TextureSource()
        {
            var rgba = Enumerable.Repeat((byte)160, 4 * 4 * 4).ToArray();
            using var s = new MemoryStream();
            DdsFile.WriteBc7(s, 4, 4, [Bc7Encoder.EncodeImage(rgba, 4, 4)]);
            _dds = s.ToArray();
        }
        public bool Exists(string p) => p == @"textures\pbr\stone.dds" || p == @"textures\stone.dds";
        public bool TryOpen(string p, out Stream stream) { stream = new MemoryStream(_dds); return Exists(p); }
        public string? Fingerprint(string p) => Exists(p) ? "fixture" : null;
    }

    [Fact]
    public void Texture_stages_write_nested_host_files_and_reuse_then_remove_them()
    {
        var assets = new TextureSource();
        var pbr = new PbrLodTextures(assets);
        Assert.NotNull(pbr.MapDiffuse(@"textures\stone.dds"));
        var first = PbrLodStage.Run(assets, pbr, _root, 256, 1f, null, default);
        Assert.Equal(1, first.Converted);
        var pbrFile = Path.Combine(_root, "textures", "anvillod", "pbr", "stone.dds");
        Assert.True(File.Exists(pbrFile));
        Assert.Equal(1, PbrLodStage.Run(assets, pbr, _root, 256, 1f, null, default).Unchanged);

        var opaque = new OpaqueLodTextures(assets, DdsAlpha.IsTranslucent);
        Assert.NotNull(opaque.MapDiffuse(@"textures\stone.dds"));
        Assert.Equal(1, OpaqueLodStage.Run(assets, opaque, _root, null, default).Written);
        var opaqueFile = Path.Combine(_root, "textures", "anvillod", "opaque", "stone.dds");
        Assert.True(File.Exists(opaqueFile));
        Assert.DoesNotContain(Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories), p => Path.GetFileName(p).Contains('\\'));
        PbrLodStage.RemoveAll(_root);
        OpaqueLodStage.RemoveAll(_root);
        Assert.False(File.Exists(pbrFile));
        Assert.False(File.Exists(opaqueFile));
    }
}
