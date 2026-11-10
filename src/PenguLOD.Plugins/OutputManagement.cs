using System.Text.Json;
using PenguLOD.Core;
using PenguLOD.Core.Paths;
using PenguLOD.Core.Pipeline;
using PenguLOD.Core.World;

namespace PenguLOD.Plugins;

public sealed record OutputScope(string[] Worlds, int[] Levels, string[] Bands);
public sealed record OutputDependency(string Stage, Dictionary<string, string> Files);
public sealed record OutputStage(string Id, string Version, string SourceFingerprint, DateTimeOffset Completed,
    OutputScope Scope, Dictionary<string, string> Files, IReadOnlyList<OutputDependency> Dependencies, bool Complete = true);
public sealed record OutputFinding(string Severity, string Message, string? Code = null);
public sealed record OutputVerification(IReadOnlyList<OutputFinding> Findings, int FilesChecked)
{
    public bool Valid => Findings.All(f => f.Severity != "Error");
    public string Summary() => $"{FilesChecked:N0} files checked · {Findings.Count(f => f.Severity == "Error")} errors\n"
        + string.Join('\n', Findings.Take(50).Select(f => f.Severity + ": " + f.Message))
        + (Findings.Count > 50 ? "\nMore findings are included in the exported report." : "");
}

public sealed class OutputSetReceipt
{
    public const string FileName = "PenguLOD.output-set.json";
    public int Schema { get; set; } = 1;
    public List<OutputStage> Stages { get; set; } = [];
    public static bool Exists(string folder) => new GamePathLookup(folder).ResolveExisting(FileName) is not null;
    private static string ReceiptPath(string folder)
    {
        string? found = new GamePathLookup(folder).ResolveExisting(FileName);
        return OutputSnapshot.Resolve(folder, found is null ? FileName : Path.GetRelativePath(folder, found).Replace('\\', '/'));
    }
    public static OutputSetReceipt Read(string folder)
    {
        OutputFiles.RequireRealPath(folder);
        string file = ReceiptPath(folder);
        if (!File.Exists(file)) throw new InvalidDataException("No output receipt. Older output can be measured, but must be regenerated into a separate folder before managed updates: " + folder);
        using var json = JsonDocument.Parse(File.ReadAllText(file));
        if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("Schema", out _))
            throw new InvalidDataException("Invalid output receipt schema.");
        var receipt = json.RootElement.Deserialize<OutputSetReceipt>(PersistentPipeline.Json);
        if (receipt is null || receipt.Schema != 1 || receipt.Stages is null || receipt.Stages.Count == 0
            || receipt.Stages.Any(s => s is null || s.Id is not ("lod" or "textures" or "terrain" or "grass")
                || s.Scope?.Worlds is null || s.Scope.Levels is null || s.Scope.Bands is null || s.Files is null || s.Files.Count == 0 || s.Dependencies is null)
            || receipt.Stages.Select(s => s.Id).Distinct().Count() != receipt.Stages.Count)
            throw new InvalidDataException("Invalid or unsupported output receipt.");
        if (receipt.Stages.Any(s => s.Scope.Worlds.Any(string.IsNullOrWhiteSpace) || s.Scope.Levels.Any(l => l is not (4 or 8 or 16 or 32))
            || s.Dependencies.Any(d => d is null || d.Stage is not ("textures" or "grass" or "terrain") || d.Files is null)))
            throw new InvalidDataException("Invalid output scope or dependencies.");
        foreach (var stage in receipt.Stages)
        {
            var actualScope = Scope(stage.Files.Keys);
            if (actualScope.Worlds.Except(stage.Scope.Worlds, StringComparer.OrdinalIgnoreCase).Any()
                || actualScope.Bands.Except(stage.Scope.Bands, StringComparer.OrdinalIgnoreCase).Any())
                throw new InvalidDataException("Output receipt scope does not cover its files.");
        }
        foreach (var files in receipt.Stages.Select(s => s.Files).Concat(receipt.Stages.SelectMany(s => s.Dependencies).Select(d => d.Files)))
        {
            if (files is null || files.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count)
                throw new InvalidDataException("Conflicting output receipt paths.");
            foreach (var (path, hash) in files)
            {
                OutputSnapshot.Resolve(folder, path);
                if (path.Equals(FileName, StringComparison.OrdinalIgnoreCase) || hash is null || hash.Length != 64 || !hash.All(Uri.IsHexDigit))
                    throw new InvalidDataException("Invalid output receipt hash or self-reference.");
            }
        }
        return receipt;
    }

    public void Write(string folder) => ReportFiles.Write(ReceiptPath(folder), this);

    public static OutputSnapshot Record(string folder, string stage, string sourceFingerprint, OutputSnapshot snapshot,
        IReadOnlyList<OutputDependency>? dependencies = null, bool complete = true, OutputScope? scope = null)
    {
        snapshot = new OutputSnapshot { Files = new(snapshot.Files, StringComparer.Ordinal) };
        foreach (string key in snapshot.Files.Keys.Where(k => k.Equals(FileName, StringComparison.OrdinalIgnoreCase)).ToArray()) snapshot.Files.Remove(key);
        var receipt = new OutputSetReceipt { Stages = [new(stage, ProductInfo.Version, sourceFingerprint, DateTimeOffset.UtcNow,
            scope ?? Scope(snapshot.Files.Keys), new(snapshot.Files, StringComparer.Ordinal), dependencies ?? [], complete)] };
        receipt.Write(folder);
        string receiptFile = ReceiptPath(folder);
        snapshot.Files.Add(Path.GetRelativePath(folder, receiptFile).Replace('\\', '/'), OutputSnapshot.HashFile(receiptFile));
        return snapshot;
    }

    public static OutputScope Scope(IEnumerable<string> files)
    {
        var worlds = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var levels = new HashSet<int>();
        var bands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in files)
        {
            string[] parts = path.Replace('\\', '/').Split('/');
            if (parts.Length == 2 && parts[0].Equals("grass", StringComparison.OrdinalIgnoreCase)
                && GrassCache.TryParseFileName(parts[1], out string grassWorld, out _, out _, out _))
                worlds.Add(grassWorld);
            if (parts.Length >= 4 && parts[0].Equals("meshes", StringComparison.OrdinalIgnoreCase) && parts[1].Equals("terrain", StringComparison.OrdinalIgnoreCase))
            {
                worlds.Add(parts[2]);
                string[] name = parts[^1].Split('.');
                if (name.Length >= 5 && int.TryParse(name[1], out int level) && level is 4 or 8 or 16 or 32)
                { levels.Add(level); bands.Add(parts[2].ToLowerInvariant() + ":" + level); }
            }
        }
        return new(worlds.Order(StringComparer.OrdinalIgnoreCase).ToArray(), levels.Order().ToArray(), bands.Order(StringComparer.Ordinal).ToArray());
    }
}

public sealed record OutputCopy(string File, string SourceFolder, string Hash);
public sealed class OutputUpdatePlan
{
    public string Destination { get; set; } = "";
    public string[] Sources { get; set; } = [];
    public string Id { get; set; } = "";
    public List<OutputStage> Stages { get; set; } = [];
    public List<OutputCopy> Files { get; set; } = [];
    public List<string> Errors { get; set; } = [];
    public List<string> Changes { get; set; } = [];
    public bool CanPublish => Errors.Count == 0;
    public string Summary() => $"{Files.Count:N0} files in the proposed package. {Errors.Count} blocking issues.\n"
        + string.Join('\n', Changes) + "\n" + string.Join('\n', Errors.Take(50))
        + (Errors.Count > 50 ? "\nMore issues are included in the exported plan." : "");
    public void Write(string path)
    { OutputManagement.ValidateReportPath(path, Sources.Append(Destination)); ReportFiles.Write(path, this); }
}

/// <summary>Plans and publishes complete stage replacements. Shared plugins and atlases are never merged.</summary>
public static class OutputManagement
{
    public static void ValidateReportPath(string path, IEnumerable<string> inputs)
    {
        OutputFiles.RequireRealPath(path);
        if (inputs.Any(root => !string.IsNullOrWhiteSpace(root) && HostPath.IsWithin(path, root)))
            throw new ArgumentException("Save reports and plans outside the source and destination folders, without overwriting input reports.");
    }
    public static OutputVerification Verify(IEnumerable<string> folders, CancellationToken ct = default)
    {
        var findings = new List<OutputFinding>(); int count = 0;
        var all = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var stages = new List<OutputStage>();
        foreach (string root in folders.Distinct())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var receipt = OutputSetReceipt.Read(root); stages.AddRange(receipt.Stages);
                foreach (var s in receipt.Stages.Where(s => !s.Complete)) findings.Add(new("Error", s.Id + " is a preview or filtered output, not a complete stage."));
                var owned = receipt.Stages.SelectMany(s => s.Files).GroupBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.OrdinalIgnoreCase);
                var actual = OutputFiles.Enumerate(root, ct).ToDictionary(p => p, StringComparer.OrdinalIgnoreCase);
                foreach (var stage in receipt.Stages)
                    foreach (var (relative, expected) in stage.Files)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (!actual.TryGetValue(relative, out var path)) { findings.Add(new("Error", "Missing " + stage.Id + " file: " + relative)); continue; }
                        string hash = OutputSnapshot.HashFile(OutputSnapshot.Resolve(root, path), ct); count++;
                        if (!hash.Equals(expected, StringComparison.OrdinalIgnoreCase)) findings.Add(new("Error", "Changed or stale file: " + relative));
                        if (all.TryGetValue(relative, out var other) && !hash.Equals(other, StringComparison.OrdinalIgnoreCase))
                            findings.Add(new("Error", "Conflicting files across selected outputs: " + relative));
                        all[relative] = hash;
                    }
                foreach (string file in actual.Keys.Where(p => !owned.ContainsKey(p) && !p.Equals(OutputSetReceipt.FileName, StringComparison.OrdinalIgnoreCase) && !p.Equals("meta.ini", StringComparison.OrdinalIgnoreCase)))
                    findings.Add(new("Error", "Unowned file; managed updates will not delete or adopt it: " + file));
            }
            catch (Exception e) when (e is IOException or InvalidDataException or JsonException or ArgumentException)
            { findings.Add(new("Error", e.Message)); }
        }
        foreach (var duplicate in stages.GroupBy(s => s.Id).Where(g => g.Count() > 1)) findings.Add(new("Error", "Multiple selected outputs own stage " + duplicate.Key + ". Select only one version."));
        foreach (var stage in stages)
            foreach (var dependency in stage.Dependencies)
            {
                var provider = stages.SingleOrDefault(s => s.Id == dependency.Stage && stages.Count(t => t.Id == s.Id) == 1);
                if (provider is null) { findings.Add(new("Error", stage.Id + " needs its matching " + dependency.Stage + " output. Include that output in verification.", "dependency-unselected")); continue; }
                foreach (var (file, expected) in dependency.Files)
                    if (!provider.Files.TryGetValue(file, out var hash) || !hash.Equals(expected, StringComparison.OrdinalIgnoreCase))
                    { findings.Add(new("Error", stage.Id + " was built with different " + dependency.Stage + " files; regenerate dependent LOD. First mismatch: " + file)); break; }
            }
        if (stages.Count == 0 && findings.Count == 0) findings.Add(new("Error", "Select at least one generated output."));
        return new(findings, count);
    }

    public static OutputUpdatePlan Plan(IEnumerable<string> sources, string destination, CancellationToken ct = default)
    {
        var plan = new OutputUpdatePlan { Destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination)), Sources = sources.Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p))).Distinct().Order(StringComparer.Ordinal).ToArray() };
        if (PenguLOD.Core.Paths.ProductPaths.HasLegacyOutput(plan.Destination))
        {
            plan.Errors.Add("This destination contains AnvilLOD-named output. Publish the PenguLOD replacement to a new folder and keep the previous set for rollback.");
            return plan;
        }
        if (plan.Sources.Length == 0) { plan.Errors.Add("Select at least one generated stage output."); return plan; }
        foreach (string source in plan.Sources)
            if (HostPath.IsWithin(source, plan.Destination) || HostPath.IsWithin(plan.Destination, source))
                plan.Errors.Add("Source and destination folders must be separate and must not contain each other.");
        if (plan.Errors.Count != 0) return plan;
        var previous = new List<OutputStage>();
        var stageFolders = new Dictionary<string, string>();
        var inventories = new SortedDictionary<string, string>(StringComparer.Ordinal);
        OutputSnapshot? destinationSnapshot = null;
        try
        {
            OutputFiles.RequireRealPath(plan.Destination);
            if (File.Exists(plan.Destination)) throw new IOException("Package destination is a file, not a folder.");
            if (Directory.Exists(plan.Destination))
            {
                destinationSnapshot = new OutputSnapshot { Files = OutputFiles.Enumerate(plan.Destination, ct)
                    .ToDictionary(p => p, p => OutputSnapshot.HashFile(OutputSnapshot.Resolve(plan.Destination, p), ct), StringComparer.Ordinal) };
                inventories[plan.Destination] = destinationSnapshot.Digest;
            }
            // Existing destinations must be entirely owned. Empty destinations are safe to populate.
            if (Directory.Exists(plan.Destination) && OutputFiles.Enumerate(plan.Destination, ct).Any(p => !p.Equals("meta.ini", StringComparison.OrdinalIgnoreCase)))
            {
                var old = OutputSetReceipt.Read(plan.Destination); previous.AddRange(old.Stages);
                foreach (var s in old.Stages) stageFolders[s.Id] = plan.Destination;
                var verify = Verify([plan.Destination], ct);
                plan.Errors.AddRange(verify.Findings.Where(f => f.Severity == "Error" && f.Code != "dependency-unselected").Select(f => f.Message));
            }
            var incoming = new List<OutputStage>();
            foreach (string root in plan.Sources)
            {
                var receipt = OutputSetReceipt.Read(root);
                foreach (var stage in receipt.Stages)
                {
                    if (incoming.Any(s => s.Id == stage.Id)) throw new InvalidDataException("More than one incoming output owns stage " + stage.Id);
                    incoming.Add(stage); stageFolders[stage.Id] = root;
                    if (previous.FirstOrDefault(s => s.Id == stage.Id) is { } old)
                    {
                        string[] missingWorlds = old.Scope.Worlds.Except(stage.Scope.Worlds, StringComparer.OrdinalIgnoreCase).ToArray();
                        int[] missingLevels = old.Scope.Levels.Except(stage.Scope.Levels).ToArray();
                        string[] missingBands = old.Scope.Bands.Except(stage.Scope.Bands, StringComparer.OrdinalIgnoreCase).ToArray();
                        if (missingWorlds.Length > 0 || missingLevels.Length > 0 || missingBands.Length > 0)
                            plan.Errors.Add($"{stage.Id}: partial replacement would lose worlds [{string.Join(", ", missingWorlds)}], levels [{string.Join(", ", missingLevels)}] or world/level pairs [{string.Join(", ", missingBands)}]. Expand generation to the union of the old and new scope. Shared plugins and atlases cannot be merged safely.");
                    }
                    plan.Changes.Add($"Replace complete {stage.Id} output: {stage.Files.Count:N0} files; worlds {string.Join(", ", stage.Scope.Worlds)}; levels {string.Join(", ", stage.Scope.Levels)}.");
                }
                inventories[root] = OutputSnapshot.Capture(root, ct: ct).Digest;
            }
            plan.Stages = previous.Where(s => incoming.All(n => n.Id != s.Id)).Concat(incoming).OrderBy(s => s.Id, StringComparer.Ordinal).ToList();
            // Verify incoming folders individually; dependencies are checked against the final stage selection below.
            foreach (string root in plan.Sources)
                plan.Errors.AddRange(Verify([root], ct).Findings.Where(f => f.Severity == "Error" && f.Code != "dependency-unselected").Select(f => f.Message));
            foreach (var stage in plan.Stages)
                foreach (var dependency in stage.Dependencies)
                {
                    var provider = plan.Stages.FirstOrDefault(s => s.Id == dependency.Stage);
                    if (provider is null || dependency.Files.Any(f => !provider.Files.TryGetValue(f.Key, out var hash) || hash != f.Value))
                        plan.Errors.Add($"{stage.Id} requires its matching {dependency.Stage} output; select that output or regenerate {stage.Id} with the replacement.");
                }
            var copies = new Dictionary<string, OutputCopy>(StringComparer.OrdinalIgnoreCase);
            foreach (var stage in plan.Stages)
                foreach (var (file, hash) in stage.Files.OrderBy(f => f.Key, StringComparer.Ordinal))
                {
                    var old = copies.GetValueOrDefault(file);
                    if (old is null) copies.Add(file, new(file, stageFolders[stage.Id], hash));
                    else if (old.Hash != hash) plan.Errors.Add("Stages contain conflicting files: " + file);
                }
            // Mod-manager metadata belongs to the destination installation, not an incoming stage.
            foreach (var file in destinationSnapshot?.Files ?? [])
                if (file.Key.Equals("meta.ini", StringComparison.OrdinalIgnoreCase))
                {
                    if (copies.TryGetValue(file.Key, out var owned) && owned.Hash != file.Value)
                        plan.Errors.Add("Incoming stage conflicts with the destination's mod-manager metadata: " + file.Key);
                    else copies[file.Key] = new(file.Key, plan.Destination, file.Value);
                }
            plan.Files = copies.Values.OrderBy(f => f.File, StringComparer.Ordinal).ToList();
            plan.Id = OutputSnapshot.HashText(JsonSerializer.Serialize(new { plan.Destination, plan.Sources, Inventories = inventories }));
        }
        catch (Exception e) when (e is IOException or InvalidDataException or JsonException or ArgumentException)
        { plan.Errors.Add(e.Message); }
        return plan;
    }

    public static string? Publish(OutputUpdatePlan reviewed, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (!reviewed.CanPublish || string.IsNullOrEmpty(reviewed.Id)) throw new InvalidDataException("Review a valid update plan before publishing.");
        OutputFiles.RequireRealPath(reviewed.Destination);
        string destination = HostPath.Canonical(reviewed.Destination);
        string parent = Path.GetDirectoryName(destination)!; Directory.CreateDirectory(parent);
        string lockPath = Path.Combine(parent, ".pengulod-output-" + OutputSnapshot.HashText(HostPath.ComparisonKey(destination))[..24] + ".lock");
        if (new FileInfo(lockPath).LinkTarget is not null) throw new IOException("Output lock must not be a symbolic link.");
        using var outputLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var current = Plan(reviewed.Sources, destination, ct);
        if (!current.CanPublish || reviewed.Id != current.Id) throw new IOException("Output or source changed after planning. Create and review a new plan.");
        using var staged = new StagedOutput(destination, false, progress, ct);
        var lookups = current.Files.Select(f => f.SourceFolder).Distinct().ToDictionary(f => f, f => new GamePathLookup(f));
        int copied = 0;
        foreach (var file in current.Files)
        {
            ct.ThrowIfCancellationRequested();
            string found = lookups[file.SourceFolder].ResolveExisting(file.File, ct) ?? throw new FileNotFoundException("Source disappeared: " + file.File);
            string from = OutputSnapshot.Resolve(file.SourceFolder, Path.GetRelativePath(file.SourceFolder, found).Replace('\\', '/'));
            string to = OutputSnapshot.Resolve(staged.Payload, file.File);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            // StagedOutput may already have copied the destination's meta.ini.
            if (!File.Exists(to)) WrittenOutputHashes.Copy(from, to, ct);
            if (OutputSnapshot.HashFile(to, ct) != file.Hash) throw new IOException("Source changed while copying: " + file.File);
            if (++copied % 100 == 0 || copied == current.Files.Count) progress?.Report($"Preparing package: {copied:N0}/{current.Files.Count:N0} files...");
        }
        ct.ThrowIfCancellationRequested();
        new OutputSetReceipt { Stages = current.Stages }.Write(staged.Payload);
        if (Plan(reviewed.Sources, destination, ct).Id != current.Id) throw new IOException("Output or source changed during preparation; nothing was published.");
        staged.Commit(progress); return staged.PreviousOutput;
    }
}
