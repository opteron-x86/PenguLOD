using System.Buffers.Binary;
using AnvilLOD.Core.Paths;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.Textures;
using AnvilLOD.Plugins;
using AnvilLOD.Textures;
using AnvilLOD.Textures.Dds;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AnvilLOD.Core.Tests;

public sealed class LinuxIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "anvillod-linux-integration-" + Guid.NewGuid().ToString("N"));
    public LinuxIntegrationTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void WineDriveMappingAndCaseResolution()
    {
        if (OperatingSystem.IsWindows()) return;
        string drive = Path.Combine(_root, "pfx", "drive_c"), game = Path.Combine(drive, "Games", "Skyrim");
        Directory.CreateDirectory(game); Directory.CreateDirectory(Path.Combine(_root, "pfx", "dosdevices"));
        Directory.CreateSymbolicLink(Path.Combine(_root, "pfx", "dosdevices", "c:"), "../drive_c");
        Assert.Equal(game, HostPath.Resolve(@"C:\games\skyrim", winePrefix: _root));
        Assert.Equal("/tmp", HostPath.Resolve(@"Z:\tmp"));
        Assert.Throws<ArgumentException>(() => HostPath.Resolve(@"D:\Game"));
        Assert.Throws<ArgumentException>(() => HostPath.Resolve(@"C:relative", winePrefix: _root));
        string alias = Path.Combine(_root, "link"); Directory.CreateSymbolicLink(alias, drive);
        Assert.True(HostPath.IsWithin(Path.Combine(alias, "Games", "Skyrim", "NewOutput"), game));
        Assert.False(HostPath.IsWithin(game + "Backup", game));
    }
    [Fact]
    public void ArchiveListsRespectIniOverridesAndEnabledPluginOrder()
    {
        string custom = Path.Combine(_root, "SkyrimCustom.ini"), standard = Path.Combine(_root, "Skyrim.ini");
        File.WriteAllText(custom, "[Archive]\nsResourceArchiveList2=My Textures.bsa\n");
        File.WriteAllText(standard, "[Archive]\nsResourceArchiveList=Skyrim - Meshes0.bsa\nsResourceArchiveList2=Unused.bsa\n");
        var ini = SkyrimArchives.ReadIniLists([custom, standard]);
        Assert.Equal(new[] { "Skyrim - Meshes0.bsa", "My Textures.bsa" }, ini);
        var ordered = SkyrimArchives.Select(GameRelease.SkyrimSE,
            new[] { "Orphan.bsa", "Second.bsa", "My Textures.bsa", "First.bsa", "Skyrim - Meshes0.bsa", "Disabled.bsa" },
            new[] { "First.esp", "Second.esp" }, ini);
        Assert.Equal(new[] { "Skyrim - Meshes0.bsa", "My Textures.bsa", "First.bsa", "Second.bsa" }, ordered);
        Assert.Contains("Skyrim - Meshes0.bsa", SkyrimArchives.ReadIniLists([]));
    }
    [Fact]
    public void GenerationRejectsGameAndUnrelatedOutputBeforeChangingAnything()
    {
        string game = Path.Combine(_root, "Game"), data = Path.Combine(game, "Data"); Directory.CreateDirectory(data);
        File.WriteAllBytes(Path.Combine(data, "Skyrim.esm"), []);
        var options = new GameContextOptions(DataFolder: data);
        Assert.Throws<InvalidOperationException>(() => GenerationDestination.Validate(options, Path.Combine(data, "Output")));
        Assert.Throws<InvalidOperationException>(() => GenerationDestination.Validate(options, _root));
        string unrelated = Path.Combine(_root, "Photos"); Directory.CreateDirectory(unrelated); File.WriteAllText(Path.Combine(unrelated, "keep.txt"), "keep");
        Assert.Throws<InvalidOperationException>(() => GenerationDestination.Validate(options, unrelated));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(unrelated, "keep.txt")));
        GenerationDestination.Validate(options, Path.Combine(_root, "New Mod"));
    }
    [Fact]
    public void PlainDataLoadsPluginsWithDifferentOnDiskCasing()
    {
        string data = Path.Combine(_root, "Data"); Directory.CreateDirectory(data);
        var master = new SkyrimMod(ModKey.FromNameAndExtension("Skyrim.esm"), SkyrimRelease.SkyrimSE);
        master.WriteToBinary(Path.Combine(data, "SKYRIM.ESM"));
        var plugin = new SkyrimMod(ModKey.FromNameAndExtension("Example.esp"), SkyrimRelease.SkyrimSE);
        plugin.WriteToBinary(Path.Combine(data, "EXAMPLE.ESP"));
        string plugins = Path.Combine(_root, "plugins.txt"); File.WriteAllText(plugins, "*example.esp\nDisabled.esp\n");
        using var context = GameContext.Open(new GameContextOptions(DataFolder: data, PluginsTxt: plugins));
        Assert.Equal(2, context.PluginsLoaded);
        Assert.Empty(context.MissingPlugins);
        Assert.Equal(new[] { "Skyrim.esm", "example.esp" }, context.LoadOrder.ListedOrder.Select(l => l.ModKey.FileName.String));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NativeBc6RepairPreservesMipChainAndSource(bool signed)
    {
        var encoder = new BcEncoder(signed ? CompressionFormat.Bc6S : CompressionFormat.Bc6U);
        encoder.Options.IsParallel = false;
        var colors = Enumerable.Repeat(new ColorRgbFloat(.25f, .5f, .75f), 16).ToArray();
        var block = encoder.EncodeBlockHdr(colors.AsSpan());
        using var sourceStream = new MemoryStream(); DdsFile.WriteBc7(sourceStream, 4, 4, [block, block, block]);
        byte[] source = sourceStream.ToArray(); BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(128), signed ? 96 : 95);
        string input = Path.Combine(_root, "Textures"); Directory.CreateDirectory(input);
        string path = Path.Combine(input, "HDR.DDS"); File.WriteAllBytes(path, source);
        var bad = TextureFixer.Scan([_root]); Assert.Single(bad);
        string output = Path.Combine(_root, "Fixed");
        using var limits = RunLimits.Enter(1);
        var results = TextureFixer.Fix(bad, "", output, null, false, nativeConverter: NativeTextureConverter.Bc6ToBc7);
        Assert.Equal("Converted to BC7", Assert.Single(results).Status); Assert.Equal(source, File.ReadAllBytes(path));
        var decoded = DdsFile.DecodeTopMip(File.ReadAllBytes(results[0].Output));
        Assert.Equal((4, 4, 3, 98), (decoded.Width, decoded.Height, decoded.Info.MipCount, decoded.Info.DxgiFormat));
        Assert.InRange((int)decoded.Rgba[0], 55, 75); Assert.InRange((int)decoded.Rgba[1], 118, 138); Assert.InRange((int)decoded.Rgba[2], 181, 201);
        BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(140), 2);
        Assert.Throws<NotSupportedException>(() => NativeTextureConverter.Bc6ToBc7(source));
    }
    [Fact]
    public async Task WorkerLimitFlowsAndCancellationStopsParallelStages()
    {
        using var cts = new CancellationTokenSource();
        using var limits = RunLimits.Enter(2, cts.Token, _root);
        Assert.Equal(_root, RunLimits.CacheDirectory);
        Assert.Equal(2, await Task.Run(() => RunLimits.Parallel().MaxDegreeOfParallelism));
        cts.Cancel(); Assert.Throws<OperationCanceledException>(() => Parallel.For(0, 10, RunLimits.Parallel(), _ => { }));
    }
}
