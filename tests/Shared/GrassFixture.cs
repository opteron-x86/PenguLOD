using System.Text;
using AnvilLOD.Plugins;

namespace AnvilLOD.Testing;

public sealed class GrassFixture : IDisposable
{
    public TextureFixture Textures { get; } = new();
    public string Root => Textures.Root;
    public string Output => Path.Combine(Root, "Grass output");
    public string Executable => Path.Combine(Root, "fake FasterNGIO");
    public GrassGenerationRequest Request => new(Textures.Game, Output, Executable, Threads: 2);
    public GrassFixture(string behavior = "success")
    {
        File.WriteAllBytes(Path.Combine(Root, "seed.cgid"), Cache());
        SetBehavior(behavior);
    }
    public void SetBehavior(string behavior)
    {
        File.WriteAllText(Executable, "#!/usr/bin/env python3\n" + $$"""
            import pathlib, sys, shutil, time
            args = sys.argv[1:]
            out = pathlib.Path(args[args.index('--out') + 1])
            print('FAKE_READY', flush=True)
            behavior = '{{behavior}}'
            if behavior == 'wait':
                time.sleep(120)
            if behavior == 'fail':
                print('fixture error', file=sys.stderr, flush=True)
                sys.exit(9)
            out.mkdir(parents=True, exist_ok=True)
            seed = pathlib.Path(__file__).with_name('seed.cgid')
            shutil.copyfile(seed, out / 'Tamrielx0000y0000.cgid')
            (out / 'Tamrielx0001y0000.cgid').write_bytes(b'\0' * 4)
            if behavior == 'corrupt':
                (out / 'Tamrielx0000y0000.cgid').write_bytes(b'\xff' * 4)
            print('wrote 2 files', flush=True)
            """ + "\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
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
