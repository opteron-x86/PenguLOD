using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AnvilLOD.Core.Paths;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.World;

namespace AnvilLOD.Plugins;

public sealed record GrassGenerationRequest(GameContextOptions Game, string OutputFolder,
    string? Executable = null, string Worldspaces = "all", string Placement = "smooth",
    string Rejection = "auto", string Seasons = "auto", string? NgioConfig = null,
    bool Preview = false, int CellX = 0, int CellY = 0, int Radius = 1,
    int Threads = 0, int Writers = 1, bool RenderGeometry = false, int? MinGrassSize = null);

public sealed record GrassGenerationResult(string OutputFolder, int Files, long Instances, int EmptyFiles,
    bool Preview, string? PreviousOutput, IReadOnlyList<string> Warnings, TimeSpan Elapsed);

public static partial class GrassGeneration
{
    public static string BundledExecutable => Path.Combine(AppContext.BaseDirectory, "backends", "fasterngio",
        OperatingSystem.IsWindows() ? "FasterNGIO.exe" : "FasterNGIO");

    internal static string[] SplitWorlds(string value) => value.Split([',', ';', ' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Validate configuration and destination location. Run checks existing contents under its output lock.</summary>
    public static void Validate(GrassGenerationRequest req)
    {
        if (req.Game.Release != Mutagen.Bethesda.GameRelease.SkyrimSE)
            throw new ArgumentException("The bundled FasterNGIO backend targets Skyrim SE / AE. VR cache generation has not been validated.");
        if (req.Placement is not ("smooth" or "vanilla") || req.Rejection is not ("auto" or "cpu" or "gpu") || req.Seasons is not ("auto" or "on" or "off"))
            throw new ArgumentException("Invalid grass placement, rejection, or seasons setting.");
        if (req.Threads is < 0 or > 1024 || req.Writers is < 1 or > 64 || req.Radius is < 0 or > 32
            || req.CellX is < -32768 or > 32767 || req.CellY is < -32768 or > 32767 || req.MinGrassSize is < 1 or > 512)
            throw new ArgumentException("Grass cache settings are out of range.");
        var worlds = SplitWorlds(req.Worldspaces);
        if (worlds.Length == 0 || worlds.Any(w => w.Equals("all", StringComparison.OrdinalIgnoreCase)) && worlds.Length != 1)
            throw new ArgumentException("Choose worldspace editor IDs (such as Tamriel), or all.");
        if (req.Preview && (worlds.Length != 1 || worlds[0].Equals("all", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("A small-area preview needs exactly one worldspace, such as Tamriel.");
        if (req.NgioConfig is { } config && !File.Exists(config)) throw new FileNotFoundException("GrassControl.ini override not found: " + config);
        GenerationDestination.Validate(req.Game, req.OutputFolder);
        if (File.Exists(Path.Combine(req.OutputFolder, TerrainGeneration.ManifestName)))
            throw new ArgumentException("Choose a separate output folder; this contains generated terrain.");
        // Full output verification is done by Run under its output lock, with progress and cancellation.
    }

    public static GrassGenerationResult Run(GrassGenerationRequest req, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var limits = RunLimits.Enter(req.Threads == 0 ? RunLimits.Parallel().MaxDegreeOfParallelism : req.Threads, ct, RunLimits.CacheDirectory);
        Validate(req);
        string exe = Path.GetFullPath(req.Executable ?? BundledExecutable);
        if (!File.Exists(exe)) throw new FileNotFoundException("FasterNGIO is missing. Use the complete PenguLOD package for your operating system, or select a native FasterNGIO executable in Grass cache > Advanced.", exe);
        if (req.Executable is null && req.Rejection == "gpu")
            throw new ArgumentException("The bundled backend uses CPU BVH. Choose Auto / CPU, or select a GPU-enabled FasterNGIO executable in Advanced.");
        var watch = Stopwatch.StartNew();
        using var preparationTiming = WorkTiming.Measure("preparation");
        var output = HostPath.Canonical(req.OutputFolder);
        var parent = Path.GetDirectoryName(output)!;
        Directory.CreateDirectory(parent);
        var lockName = ".anvillod-grass-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(HostPath.ComparisonKey(output))))[..16] + ".lock";
        var lockPath = Path.Combine(parent, lockName);
        if (new FileInfo(lockPath).LinkTarget is not null) throw new IOException("Grass lock must not be a symbolic link: " + lockPath);
        using var guard = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        GrassGenerationManifest.CheckDestination(output, req.Preview, progress, ct);
        var work = Path.Combine(parent, ".anvillod-grass-build-" + Guid.NewGuid().ToString("N"));
        var payload = Path.Combine(work, "output");
        var grass = Path.Combine(payload, "grass");
        Directory.CreateDirectory(grass);
        try
        {
            var input = PrepareInput(req, Path.Combine(work, "input"), progress, ct);
            var args = Arguments(req, input, grass);
            var warnings = new List<string>();
            if (req.Executable is null) progress?.Report("Bundled FasterNGIO: native CPU BVH collision rejection.");
            using (var log = new StreamWriter(Path.Combine(payload, "FasterNGIO.log")) { AutoFlush = true })
            {
                log.WriteLine("Executable: " + exe);
                log.WriteLine("Arguments: " + JsonSerializer.Serialize(args));
                log.WriteLine("Source: " + input.Source);
                preparationTiming.Dispose();
                using var generationTiming = WorkTiming.Measure("generation");
                RunProcess(exe, args, work, line =>
                {
                    lock (log)
                    {
                        log.WriteLine(line);
                        if (line.Contains("[warning]", StringComparison.OrdinalIgnoreCase) || line.Contains("[error]", StringComparison.OrdinalIgnoreCase)) warnings.Add(line);
                    }
                    progress?.Report(line);
                }, ct);
            }
            ct.ThrowIfCancellationRequested();
            using var outputTiming = WorkTiming.Measure("output-verification-publication");
            progress?.Report("Validating grass cache files...");
            var manifest = GrassGenerationManifest.Create(payload, input.Source, input.LoadOrderFingerprint, req, ct);
            if (manifest.Files.Count == 0) throw new InvalidOperationException("FasterNGIO produced no cache files. Check the selected worldspace/cells and NGIO settings. Previous output was preserved.");
            if (manifest.Instances == 0)
            {
                const string warning = "The selected area produced only empty grass caches. Check worldspaces, grass models and placement settings before installing this output.";
                warnings.Add(warning); progress?.Report("WARNING: " + warning);
            }
            manifest.Save(payload);
            File.WriteAllText(Path.Combine(payload, "README-Grass.txt"),
                $"AnvilLOD grass cache — FasterNGIO\nSource: {input.Source}\nWorldspaces: {req.Worldspaces}\n" +
                (req.Preview ? "SMALL-AREA PREVIEW. This is not a complete grass cache.\n" : "Install this folder as a separate mod. Disable the old grass cache instead of merging files.\n") +
                "Keep NGIO and Grass Cache Helper NG installed as required by your setup. This output does not change game or NGIO settings.\n" +
                "Regenerate after changes to grass, landscape, placement settings, or load order. Check the log and inspect grass in game.\n");
            // Recheck the destination after the potentially long process. Nothing is replaced until
            // every file is validated. Publication is a directory swap on the same filesystem.
            ct.ThrowIfCancellationRequested();
            GrassGenerationManifest.CheckDestination(output, req.Preview, progress, ct);
            var metadata = Path.Combine(output, "meta.ini");
            if (File.Exists(metadata)) File.Copy(metadata, Path.Combine(payload, "meta.ini"));
            string? previous = null;
            if (Directory.Exists(output))
            {
                previous = output + ".previous-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6];
                Directory.Move(output, previous);
            }
            try { Directory.Move(payload, output); }
            catch
            {
                if (previous is not null) Directory.Move(previous, output);
                throw;
            }
            progress?.Report($"Grass cache published: {manifest.Files.Count:N0} files, {manifest.Instances:N0} instances in {watch.Elapsed.TotalSeconds:F1}s.");
            if (previous is not null) progress?.Report("Previous output retained: " + previous);
            return new(output, manifest.Files.Count, manifest.Instances, manifest.EmptyFiles, req.Preview, previous, warnings, watch.Elapsed);
        }
        finally
        {
            // .NET removes file symlinks themselves; it does not delete their targets.
            try { Directory.Delete(work, recursive: true); }
            catch (IOException ex) { progress?.Report("Could not remove temporary grass workspace: " + work + ": " + ex.Message); }
        }
    }

    internal static List<string> Arguments(GrassGenerationRequest req, GrassGenerationInput input, string grass)
    {
        var args = new List<string> { "--data", input.Data, "--plugins", input.Plugins, "--out", grass,
            "--game-ini-dir", input.Inis, "--ngio-config", input.Ngio, "--world", input.WorldArgument,
            "--placement", req.Placement, "--seasons", req.Seasons,
            "--threads", (req.Threads == 0 ? RunLimits.Parallel().MaxDegreeOfParallelism : req.Threads).ToString(CultureInfo.InvariantCulture),
            "--writers", req.Writers.ToString(CultureInfo.InvariantCulture), "--write-empty-cells" };
        // Explicit empty files prevent older installed caches from winning where this run found no grass.
        // Omitting --reject for Auto preserves GrassControl.ini's Ray-cast-enabled setting.
        if (req.Rejection != "auto") args.AddRange(["--reject", req.Rejection]);
        if (req.Preview) args.AddRange(["--radius", req.CellX.ToString(CultureInfo.InvariantCulture), req.CellY.ToString(CultureInfo.InvariantCulture), req.Radius.ToString(CultureInfo.InvariantCulture)]);
        if (req.RenderGeometry) args.Add("--render-geometry");
        if (req.MinGrassSize is { } density) args.AddRange(["--min-grass-size", density.ToString(CultureInfo.InvariantCulture)]);
        return args;
    }

    private static void RunProcess(string exe, IReadOnlyList<string> args, string work, Action<string> report, CancellationToken ct)
    {
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(exe)! };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        // Keep runtime shader/user caches confined to this job as well.
        start.Environment["XDG_CACHE_HOME"] = Path.Combine(work, "cache");
        start.Environment["XDG_CONFIG_HOME"] = Path.Combine(work, "config");
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new IOException("Could not start FasterNGIO.");
        using var registration = ct.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        });
        var tail = new Queue<string>();
        async Task Pump(StreamReader reader)
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                lock (tail) { tail.Enqueue(line); if (tail.Count > 12) tail.Dequeue(); }
                report(line);
            }
        }
        var stdout = Pump(process.StandardOutput); var stderr = Pump(process.StandardError);
        try { Task.WhenAll(stdout, stderr, process.WaitForExitAsync()).GetAwaiter().GetResult(); }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.WaitForExit(); throw;
        }
        ct.ThrowIfCancellationRequested();
        if (process.ExitCode != 0) throw new InvalidOperationException($"FasterNGIO failed (exit {process.ExitCode}); previous output was preserved.\n" + string.Join('\n', tail));
    }
}
