using System.Diagnostics;
using System.Text.Json;
using AnvilLOD.Core.Paths;

namespace AnvilLOD.Core.Pipeline;

public sealed record PipelineStagePlan(string Id, string Title, string OutputFolder, string[] Dependencies);
public sealed class PipelineStageState
{
    public required PipelineStagePlan Plan { get; set; }
    public string Status { get; set; } = "Pending";
    public string InputFingerprint { get; set; } = "";
    public Dictionary<string, string> Dependencies { get; set; } = [];
    public OutputSnapshot? Output { get; set; }
    public JsonElement? Result { get; set; }
    public string? Error { get; set; }
    public bool Reused { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public double VerificationSeconds { get; set; }
    public double ProcessingSeconds { get; set; }
    public double CheckpointSeconds { get; set; }
    public Dictionary<string, double> Timings { get; set; } = [];
}
public sealed class PipelineJournal
{
    public int SchemaVersion { get; set; } = 1;
    public string ApplicationVersion { get; set; } = ProductInfo.Version;
    public DateTimeOffset UpdatedAt { get; set; }
    public List<PipelineStageState> Stages { get; set; } = [];
}

/// <summary>A durable dependency graph. Completed stages are reused only after content verification.</summary>
public sealed class PersistentPipeline : IDisposable
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly List<FileStream> _locks = [];
    private readonly IProgress<string>? _progress;
    private readonly CancellationToken _ct;
    private readonly bool _reuse;
    public string Folder { get; }
    public string JournalPath => Path.Combine(Folder, "job.json");
    public PipelineJournal Journal { get; private set; }

    public PersistentPipeline(string cache, IEnumerable<PipelineStagePlan> plan, bool reuse = true,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        _ct = ct; _progress = progress; _reuse = reuse;
        var stages = plan.Select(p => p with { OutputFolder = HostPath.Canonical(p.OutputFolder) }).ToArray();
        var seen = new HashSet<string>();
        foreach (var stage in stages)
        {
            if (!seen.Add(stage.Id) || stage.Dependencies.Any(d => d == stage.Id || !seen.Contains(d)))
                throw new ArgumentException("Pipeline stages must have unique IDs and depend on earlier stages.");
        }
        if (stages.Length == 0) throw new ArgumentException("Select at least one generation stage.");
        string key = OutputSnapshot.HashText(JsonSerializer.Serialize(stages))[..24];
        Folder = Path.Combine(cache, "jobs", key);
        Journal = new() { Stages = stages.Select(p => new PipelineStageState { Plan = p }).ToList() };
        try
        {
            // Lock output locations as well as the journal: another cache directory must not bypass the guard.
            foreach (var output in stages.Select(p => p.OutputFolder).Order(StringComparer.Ordinal).Distinct())
            {
                ct.ThrowIfCancellationRequested();
                var parent = Path.GetDirectoryName(output)!; Directory.CreateDirectory(parent);
                Lock(Path.Combine(parent, ".pengulod-output-" + OutputSnapshot.HashText(output)[..24] + ".lock"));
            }
            Directory.CreateDirectory(Folder); Lock(Path.Combine(Folder, "job.lock"));
            if (File.Exists(JournalPath))
            {
                try
                {
                    var previous = JsonSerializer.Deserialize<PipelineJournal>(File.ReadAllText(JournalPath), Json);
                    if (previous is null || previous.SchemaVersion != 1 || previous.Stages is null
                        || previous.Stages.Any(s => s is null || s.Plan is null || s.Dependencies is null || s.Timings is null
                            || s.Output is { Files: null })
                        || JsonSerializer.Serialize(previous.Stages.Select(s => s.Plan)) != JsonSerializer.Serialize(stages))
                        throw new InvalidDataException("Checkpoint schema or plan does not match.");
                    Journal = previous;
                }
                catch (Exception e) when (e is JsonException or InvalidDataException)
                {
                    File.Copy(JournalPath, JournalPath + ".invalid-" + Guid.NewGuid().ToString("N"));
                    progress?.Report("Checkpoint could not be reused; starting a new record. " + e.Message);
                }
            }
            foreach (var stage in Journal.Stages)
            {
                stage.Reused = false;
                if (stage.Status == "Running") { stage.Status = "Interrupted"; stage.Error = "The previous process stopped before this stage completed."; }
            }
            Save(); progress?.Report("Persistent job: " + JournalPath);
        }
        catch { Dispose(); throw; }
    }

    private void Lock(string file)
    {
        if (new FileInfo(file).LinkTarget is not null) throw new IOException("Job lock must not be a symbolic link: " + file);
        try { _locks.Add(new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)); }
        catch (IOException e) { throw new IOException("Another generation job is using this output or checkpoint: " + file, e); }
    }

    public T Run<T>(string id, string inputFingerprint, Func<T> generate, Func<T, OutputSnapshot>? capture = null, bool allowReuse = true)
    {
        _ct.ThrowIfCancellationRequested();
        var stage = Journal.Stages.Single(s => s.Plan.Id == id);
        var dependencies = stage.Plan.Dependencies.ToDictionary(d => d, d =>
        {
            var parent = Journal.Stages.Single(s => s.Plan.Id == d);
            if (parent.Status != "Complete" || parent.Output is null) throw new InvalidOperationException("Dependency is not complete: " + d);
            return parent.InputFingerprint + ":" + parent.Output.Digest;
        });
        string fingerprint = OutputSnapshot.HashText(ProductInfo.Version + "|pipeline-v1|" + inputFingerprint);
        var verify = Stopwatch.StartNew();
        if (_reuse && allowReuse && stage.Status == "Complete" && stage.InputFingerprint == fingerprint && stage.Output is not null && stage.Result is not null
            && dependencies.Count == stage.Dependencies.Count && dependencies.All(k => stage.Dependencies.GetValueOrDefault(k.Key) == k.Value))
        {
            try
            {
                _progress?.Report("Verifying completed " + stage.Plan.Title + "...");
                stage.Output.Verify(stage.Plan.OutputFolder, _progress, _ct);
                T result = stage.Result.Value.Deserialize<T>(Json) ?? throw new InvalidDataException("Saved result is empty.");
                stage.Reused = true; stage.VerificationSeconds = verify.Elapsed.TotalSeconds; Save();
                _progress?.Report($"Reused {stage.Plan.Title}: {stage.Output.Files.Count:N0} verified files ({verify.Elapsed.TotalSeconds:F1}s verification).");
                return result;
            }
            catch (Exception e) when (e is IOException or JsonException or InvalidDataException)
            { _progress?.Report("Cannot reuse " + stage.Plan.Title + ": " + e.Message); }
        }
        stage.Status = "Running"; stage.Reused = false; stage.Error = null; stage.Output = null; stage.Result = null;
        stage.InputFingerprint = fingerprint; stage.Dependencies = dependencies; stage.Attempts++;
        stage.StartedAt = DateTimeOffset.UtcNow; stage.FinishedAt = null;
        stage.VerificationSeconds = verify.Elapsed.TotalSeconds; stage.ProcessingSeconds = 0; stage.CheckpointSeconds = 0; stage.Timings.Clear();
        // Persist invalidation before starting work; a crash cannot leave descendants appearing reusable.
        InvalidateDescendants(id); Save();
        _progress?.Report("Starting " + stage.Plan.Title + "...");
        var processing = Stopwatch.StartNew();
        using var timing = WorkTiming.Begin(stage.Timings);
        try
        {
            T result = generate(); _ct.ThrowIfCancellationRequested();
            stage.ProcessingSeconds = processing.Elapsed.TotalSeconds;
            var checkpoint = Stopwatch.StartNew();
            stage.Output = capture is null ? OutputSnapshot.Capture(stage.Plan.OutputFolder, _progress, _ct) : capture(result);
            if (stage.Output.Files.Count == 0) throw new InvalidDataException("Cannot checkpoint an empty stage output.");
            stage.Result = JsonSerializer.SerializeToElement(result, Json);
            stage.CheckpointSeconds = checkpoint.Elapsed.TotalSeconds;
            stage.Status = "Complete"; stage.FinishedAt = DateTimeOffset.UtcNow; Save();
            _progress?.Report($"Completed {stage.Plan.Title}: {stage.ProcessingSeconds:F1}s processing, {stage.CheckpointSeconds:F1}s checkpointing.");
            return result;
        }
        catch (Exception e)
        {
            stage.Status = e is OperationCanceledException ? "Cancelled" : "Failed";
            stage.Error = e.Message; stage.ProcessingSeconds = processing.Elapsed.TotalSeconds;
            stage.FinishedAt = DateTimeOffset.UtcNow; Save(); throw;
        }
    }

    private void InvalidateDescendants(string id)
    {
        var invalid = new HashSet<string> { id };
        foreach (var stage in Journal.Stages)
            if (stage.Plan.Dependencies.Any(invalid.Contains))
            { invalid.Add(stage.Plan.Id); stage.Status = "Invalidated"; stage.Reused = false; }
    }

    public void Save()
    {
        Journal.UpdatedAt = DateTimeOffset.UtcNow;
        Journal.ApplicationVersion = ProductInfo.Version;
        string temp = JournalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(Journal, Json)); File.Move(temp, JournalPath, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public void Dispose() { foreach (var stream in _locks) stream.Dispose(); _locks.Clear(); }
}

/// <summary>Measured operation durations. Concurrent I/O is reported as summed worker time, never an ETA.</summary>
public static class WorkTiming
{
    private static readonly AsyncLocal<Dictionary<string, double>?> Current = new();
    public static IDisposable Begin(Dictionary<string, double> target)
    { var previous = Current.Value; Current.Value = target; return new Scope(() => Current.Value = previous); }
    public static IDisposable Measure(string phase)
    {
        var target = Current.Value; var watch = Stopwatch.StartNew();
        return new Scope(() => { if (target is not null) lock (target) target[phase] = target.GetValueOrDefault(phase) + watch.Elapsed.TotalSeconds; });
    }
    private sealed class Scope(Action end) : IDisposable
    { private Action? _end = end; public void Dispose() => Interlocked.Exchange(ref _end, null)?.Invoke(); }
}
