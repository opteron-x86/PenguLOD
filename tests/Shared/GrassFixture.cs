using System.Text;
using AnvilLOD.Plugins;

namespace AnvilLOD.Testing;

public sealed class GrassFixture : IDisposable
{
    public TextureFixture Textures { get; } = new();
    public string Root => Textures.Root;
    public string Output => Path.Combine(Root, "Grass output");
    private string Backend => Path.Combine(Root, "Fake backend forêt");
    public string Executable => Path.Combine(Backend, OperatingSystem.IsWindows() ? "PenguLOD.GrassFixture.exe" : "PenguLOD.GrassFixture");
    public GrassGenerationRequest Request => new(Textures.Game, Output, Executable, Threads: 2);
    public GrassFixture(string behavior = "success")
    {
        Directory.CreateDirectory(Backend);
        foreach (string source in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "grass-fixture")))
            File.Copy(source, Path.Combine(Backend, Path.GetFileName(source)));
        File.WriteAllBytes(Path.Combine(Backend, "seed.cgid"), Cache());
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        SetBehavior(behavior);
    }
    public void SetBehavior(string behavior)
    {
        File.WriteAllText(Path.Combine(Backend, "behavior.txt"), behavior);
    }
    public static byte[] Cache(uint count = 1, uint halves = 16)
    {
        using var output = new MemoryStream(); using var w = new BinaryWriter(output);
        w.Write(1u); var model = Encoding.ASCII.GetBytes("grass\\test.nif\0"); w.Write(model.Length); w.Write(model);
        w.Write(1f); w.Write(0x00000800u); w.Write(new byte[] { 1, 1, 0 }); w.Write(1u);
        for (int i = 0; i < 7; i++) w.Write(0f);
        w.Write(count); w.Write(halves);
        for (int i = 0; i < 16; i++) w.Write(BitConverter.HalfToUInt16Bits((Half)(i == 3 || i == 4 ? 1 : 0)));
        return output.ToArray();
    }
    public void Dispose() => Textures.Dispose();
}
