using System.Numerics;
using PenguLOD.Core.Lod;
using PenguLOD.Core.Pipeline;
using PenguLOD.Core.World;
using PenguLOD.Plugins;

namespace PenguLOD.Core.Tests;

/// <summary>
/// The SKSE runtime reads PenguLOD.dyn, .copies and .native. These fixed inputs go through the real writers and must give the bytes
/// committed in skse/tests/fixtures, which the runtime's own Linux tests (tools/test-runtime.sh) parse with the C++ reader.
/// Set PENGULOD_UPDATE_RUNTIME_FIXTURES=1 to rewrite the fixtures after an intended format change, and update the runtime with it.
/// </summary>
public sealed class RuntimeDataGoldenTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pengulod-golden-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private static string Fixtures()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "PenguLOD.slnx"))) return Path.Combine(dir.FullName, "skse", "tests", "fixtures");
        throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static LodReference Ref(string id, string mesh, Vector3 position, Vector3 rotation, float scale) =>
        new($"{id}:Skyrim.esm", "base", null, "Tamriel", "Skyrim.esm", position, rotation, scale, new LodMeshSet(mesh, null, null, null), LodReferenceFlags.None);

    private void Check(string relative, string name)
    {
        string written = Path.Combine(_root, relative.Replace('\\', Path.DirectorySeparatorChar)), golden = Path.Combine(Fixtures(), name);
        if (Environment.GetEnvironmentVariable("PENGULOD_UPDATE_RUNTIME_FIXTURES") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(golden)!);
            File.Copy(written, golden, overwrite: true);
        }
        Assert.True(File.Exists(golden), "Missing fixture " + golden + "; run once with PENGULOD_UPDATE_RUNTIME_FIXTURES=1.");
        Assert.Equal(File.ReadAllBytes(golden), File.ReadAllBytes(written));
    }

    [Fact]
    public void DynamicLodFileMatchesTheRuntimeFixture()
    {
        var refs = new[]
        {
            // A switchable reference: enable parent, opposite state, initially disabled.
            new DynamicLodReference(Ref("00012345", @"meshes\lod\farmhouse\farmhouse01_lod_0.nif", new(100.5f, -200.25f, 300f), new(0.1f, 0.2f, 0.3f), 1.25f),
                "Skyrim.esm", 0x12345, "Skyrim.esm", 0x3C, ParentPlugin: "Update.esm", ParentLocalId: 0x55, ParentOpposite: true, InitiallyDisabled: true),
            // A near grid object from a plugin with a non-ASCII name (UTF-8 length prefix), and window glow at any distance.
            new DynamicLodReference(Ref("00000801", @"meshes\effects\fxwaterfallbodytall.nif", new(-4096f, 8192f, -64f), Vector3.Zero, 1f),
                "Wässerfälle.esp", 0x801, "Skyrim.esm", 0x3C, Grid: DynamicGrid.Near),
            new DynamicLodReference(Ref("00000802", @"meshes\architecture\farmhouse\farmhouse01glow_lod_0.nif", new(1f, 2f, 3f), Vector3.Zero, 2f),
                "Skyrim.esm", 0x802, "Skyrim.esm", 0x3C, Grid: DynamicGrid.NeverFade, Glow: true),
        };
        Assert.Equal(3, DynamicLodWriter.Write(_root, refs));
        Check(DynamicLodWriter.RelativePath, "PenguLOD.dyn");
    }

    [Fact]
    public void ParentCopyFileMatchesTheRuntimeFixture()
    {
        var copies = new List<ParentCopy>
        {
            new("WhiterunWorld", "Skyrim.esm", 0x1A26F, "Skyrim.esm", 0x123456, "RockL01", @"Meshes\Landscape\Rocks\RockL01.nif",
                new Vector3(1, 2, 3), new Vector3(0.1f, 0.2f, 0.3f), 1.5f, 6, -1, true, false),
            new("WhiterunWorld", "Skyrim.esm", 0x1A26F, "Skyrim.esm", 0x123457, "RockPileM02Snow", @"meshes\landscape\rocks\rockpilem02.nif",
                new Vector3(4, 5, 6), Vector3.Zero, 1f, 7, -1, false, true),
        };
        Assert.Equal(2, ParentCopyWriter.Write(_root, copies));
        Check(ParentCopyWriter.RelativePath, "PenguLOD.copies");
    }

    [Fact]
    public void NativeGridObjectFileMatchesTheRuntimeFixture()
    {
        var source = new DynamicLodReference(Ref("0001F00D", @"meshes\effects\fxwaterfallbodytall.nif", new(10f, 20f, 30f), Vector3.Zero, 1f),
            "Skyrim.esm", 0x1F00D, "Skyrim.esm", 0x3C, ParentPlugin: "Skyrim.esm", ParentLocalId: 0x1F00E, ParentOpposite: true);
        var plain = new DynamicLodReference(Ref("0001F00F", @"meshes\effects\fxfire.nif", new(-5f, -6f, -7f), Vector3.Zero, 1f),
            "Skyrim.esm", 0x1F00F, "Skyrim.esm", 0x3C, InitiallyDisabled: true);
        var entries = new[]
        {
            new NativeGridObjectWriter.Entry(new NativeGridObjects.Pick(source, @"effects\fxwaterfallbodytall.nif", Movable: false), 0x801, "PenguLOD.esp"),
            new NativeGridObjectWriter.Entry(new NativeGridObjects.Pick(plain, @"effects\fxfire.nif", Movable: false, Near: true), 0x802, "PenguLOD Effects.esp"),
        };
        Assert.Equal(2, NativeGridObjectWriter.Write(_root, entries));
        Check(NativeGridObjectWriter.RelativePath, "PenguLOD.native");
    }
}
