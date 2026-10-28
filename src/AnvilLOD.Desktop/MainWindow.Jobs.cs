using System.Text.Json;
using AnvilLOD.Core.Lod;
using AnvilLOD.Core.Paths;
using AnvilLOD.Core.Textures;
using AnvilLOD.Core.World;
using AnvilLOD.Desktop.Models;
using AnvilLOD.Desktop.Services;
using AnvilLOD.Meshes.Authoring;
using AnvilLOD.Plugins;
using AnvilLOD.Plugins.Authoring;
using AnvilLOD.Plugins.Mo2;
using AnvilLOD.Textures;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace AnvilLOD.Desktop;

public sealed partial class MainWindow
{
    private async Task RunJob<T>(string title, DesktopSettings snapshot, Func<IProgress<string>, CancellationToken, T> work,
        Action<T, string, TimeSpan> completed)
    {
        if (_runner.IsRunning) throw new InvalidOperationException("Wait for the active job to finish.");
        GenerationOptions.Validate(snapshot);
        Save(); _page.IsEnabled = false; if (_lodActions is not null) _lodActions.IsEnabled = false; _cancel.IsEnabled = true; _progress.IsVisible = true;
        _watch.Restart(); _status.Text = title; Notice("");
        try
        {
            var cache = RequestFactory.Optional(snapshot.CacheFolder) is { } c ? RequestFactory.PathValue(c, snapshot) : SettingsStore.CacheRoot;
            var result = await _runner.Run(title, cache, (int)snapshot.MaxThreads, work, _messages.Enqueue);
            _lastRunFolder = result.Folder;
            DrainLog();
            Directory.CreateDirectory(result.Folder);
            SettingsStore.Write(Path.Combine(result.Folder, "settings.json"), snapshot);
            if (result.Cancelled)
            {
                _status.Text = "Cancelled";
                Notice("Job cancelled. Partial output may remain; rerun generation before installing it.");
                File.AppendAllText(Path.Combine(result.Folder, "run.log"), "\nCANCELLED\n");
            }
            else if (result.Error is { } error)
            {
                _status.Text = title + " failed"; Notice(error.Message); _messages.Enqueue(error.ToString());
                File.AppendAllText(Path.Combine(result.Folder, "run.log"), "\nERROR: " + error + "\n");
            }
            else
            {
                _status.Text = title + " completed";
                completed(result.Value!, result.Folder, result.Elapsed);
            }
        }
        finally
        {
            _watch.Stop(); _elapsed.Text = _watch.Elapsed.ToString(@"hh\:mm\:ss");
            _page.IsEnabled = true; if (_lodActions is not null) _lodActions.IsEnabled = true; _cancel.IsEnabled = false; _progress.IsVisible = false;
            if (_closing) { _allowClose = true; Close(); }
        }
    }

    internal Task RunScan(bool generate)
    {
        var s = _settings.Snapshot();
        if (generate) return RunPersistentPipeline("LOD generation", s, state => new GenerationPipelineRequest(
            Lod: RequestFactory.Scan(state, true), Resume: state.ResumeCompletedStages, VerifySourceContents: state.VerifySourceContents));
        return RunJob(generate ? "LOD generation" : "Load order scan", s,
            (progress, ct) =>
            {
                progress.Report("Checking LOD settings and output paths...");
                var request = RequestFactory.Scan(s, generate);
                ct.ThrowIfCancellationRequested();
                return ScanPipeline.Run(request, progress, ct);
            }, (summary, folder, elapsed) =>
            {
                ScanReport.Write(Path.Combine(folder, "scan-report.json"), summary, elapsed);
                ShowSummary(summary);
                if (summary.Generation is { BlocksFailed: > 0 })
                { _status.Text = "Generation finished with failed blocks"; Notice("Some blocks failed. Review the report and log before installing this output."); }
                else if (summary.Grids.Count == 0)
                { _status.Text = "No matching worldspaces found"; Notice("No LOD settings matched the requested worldspaces. Check the names, game files and archive list."); }
                else if (summary.Warnings.Count + summary.MissingPlugins.Count > 0 || summary.Stats.SkippedMissingMesh > 0 || summary.Generation?.MeshErrors.Count > 0)
                { _status.Text = (generate ? "Generation" : "Scan") + " completed with warnings"; Notice("Review the warnings and missing assets in Scan results. Full details are in the run folder."); }
                else Notice(generate ? "Generation complete. Install the output as a mod and enable any generated plugins." : "Scan complete. Review the worldspaces below, then generate LOD.");
            });
    }
    internal void ShowSummary(ScanSummary s)
    {
        _counts.Text = $"{s.PluginsLoaded:N0} plugins  ·  {s.ArchivesIndexed:N0} archives  ·  {s.Stats.LodReferencesFound:N0} LOD references\n{s.Plan.Rebuild.Count:N0} blocks to rebuild  ·  {s.Plan.Unchanged.Count:N0} unchanged";
        if (s.Generation is { } g) _counts.Text += $"  ·  {g.BlocksWritten:N0} written  ·  {g.Triangles:N0} triangles";
        _worlds.ItemsSource = s.Grids.Keys.Order().Select(w => new WorldRow(w,
            s.Quads.Count(q => q.Key.Worldspace == w && q.Key.Level == LodLevel.Lod4), s.Quads.Count(q => q.Key.Worldspace == w && q.Key.Level == LodLevel.Lod8),
            s.Quads.Count(q => q.Key.Worldspace == w && q.Key.Level == LodLevel.Lod16), s.Quads.Count(q => q.Key.Worldspace == w && q.Key.Level == LodLevel.Lod32))).ToArray();
        var issues = s.Warnings.Concat(s.MissingPlugins.Select(p => "Missing plugin: " + p))
            .Concat(s.Stats.MissingMeshes.OrderByDescending(m => m.Value).Take(100).Select(m => $"Missing mesh ({m.Value:N0} refs): {m.Key}"))
            .Concat(s.Generation?.MeshErrors.Select(m => "Mesh error: " + m) ?? [])
            .Concat(s.Generation?.BlockErrors.Select(m => "Block error: " + m) ?? []);
        _treeCoverage.ItemsSource = s.Tree3D?.Levels;
        _issues.Text = string.Join('\n', issues);
    }
    public sealed record WorldRow(string World, int Lod4, int Lod8, int Lod16, int Lod32);

    private void LoadProfiles()
    {
        // Profile discovery needs only MO2 configuration, not plugin loading.
        var game = RequestFactory.Game(_settings);
        var instance = Mo2Instance.Open(game.Mo2InstanceFolder!, game.Mo2Locations);
        var profiles = instance.ListProfiles().ToArray();
        _profiles.ItemsSource = profiles;
        if (string.IsNullOrWhiteSpace(_settings.Profile)) _settings.Profile = instance.SelectedProfile ?? profiles.FirstOrDefault() ?? "";
        _profiles.SelectedItem = profiles.FirstOrDefault(p => p.Equals(_settings.Profile, StringComparison.OrdinalIgnoreCase));
        Notice($"Found {profiles.Length} profiles. Game: {instance.GamePath}");
    }
    private void ApplyPreset(string preset)
    {
        _settings.Options.Clear();
        void Set(string key, object value) => _settings.Options[key] = JsonSerializer.SerializeToElement(value);
        if (preset == "Balanced") { Set("preset", "Medium"); Set("grassDensity", .05m); Set("detailedShips", false); }
        if (preset == "Objects")
            foreach (string k in new[] { "trees", "tree3d", "grass", "csGrass", "dynamic", "grid", "nativeGrid", "animatedShips", "renderBillboards" }) Set(k, false);
        _pages["Generation settings"] = GenerationSettings(); SelectPage("Generation settings"); ScheduleSave();
        Notice(preset + " preset applied. Source and output paths were kept.");
    }
    private async Task AddInput(string property, bool files)
    {
        string[] paths;
        if (files)
        {
            var selected = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Select NIF models", AllowMultiple = true, FileTypeFilter = [new FilePickerFileType("NIF models") { Patterns = ["*.nif", "*.NIF"] }] });
            paths = selected.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray();
        }
        else { var selected = await Pick(false, "Add input folder"); paths = selected is null ? [] : [selected]; }
        var info = typeof(DesktopSettings).GetProperty(property)!;
        info.SetValue(_settings, string.Join('\n', RequestFactory.Lines((string)info.GetValue(_settings)!).Concat(paths).Distinct()));
    }
    private static string[] Inputs(string text, DesktopSettings s)
    {
        var paths = RequestFactory.Lines(text).Select(p => RequestFactory.PathValue(p, s)).Distinct().ToArray();
        if (paths.Length == 0) throw new ArgumentException("Add at least one input file or folder.");
        foreach (var p in paths) if (!File.Exists(p) && !Directory.Exists(p)) throw new FileNotFoundException("Input not found: " + p);
        return paths;
    }
    private static string ToolOutput(string value, DesktopSettings s, IEnumerable<string> inputs)
    {
        string output = RequestFactory.RequiredPath(value, "an output folder", s);
        foreach (var input in inputs)
        {
            string root = File.Exists(input) ? Path.GetDirectoryName(input)! : input;
            if (HostPath.IsWithin(output, root) || HostPath.IsWithin(root, output)) throw new ArgumentException("Choose a separate output folder outside the source: " + root);
        }
        // Also protect the configured game and the mod manager's profile files.
        if (RequestFactory.Optional(s.GameFolder) is { } game)
        {
            string path = RequestFactory.PathValue(game, s);
            if (Path.GetFileName(Path.TrimEndingDirectorySeparator(path)).Equals("Data", StringComparison.OrdinalIgnoreCase)) path = Path.GetDirectoryName(path)!;
            if (HostPath.IsWithin(output, path) || HostPath.IsWithin(path, output)) throw new ArgumentException("Tool output must be separate from the game installation.");
        }
        if (s.SourceMode == "Mod Organizer 2" && RequestFactory.Optional(s.Instance) is not null)
        {
            var g = RequestFactory.Game(s); var mo2 = Mo2Instance.Open(g.Mo2InstanceFolder!, g.Mo2Locations);
            if (HostPath.IsWithin(output, mo2.GamePath) || HostPath.IsWithin(output, mo2.ProfilesFolder)
                || new[] { mo2.InstanceFolder, mo2.ModsFolder, mo2.ProfilesFolder, mo2.GamePath }.Any(root => HostPath.IsWithin(root, output)))
                throw new ArgumentException("Tool output must be a separate mod folder, outside the game and profile files.");
            foreach (var mod in mo2.OpenProfile(g.Mo2Profile).EnabledModFolders())
                if (HostPath.IsWithin(output, mod)) throw new ArgumentException("Choose output outside enabled source mods: " + mod);
        }
        return output;
    }
    private Task RunMaker()
    {
        var s = _settings.Snapshot(); var inputs = Inputs(s.MakerInputs, s); var output = ToolOutput(s.MakerOutput, s, inputs);
        if (string.IsNullOrWhiteSpace(s.MakerGroup) || s.MakerGroup.IndexOfAny(['/', '\\']) >= 0 || s.MakerGroup is "." or "..") throw new ArgumentException("Use a simple group name without path separators.");
        var levels = s.MakerLevels.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).Distinct().ToArray();
        if (levels.Length == 0 || levels.Any(l => l < 0 || l > 2)) throw new ArgumentException("Mesh levels must be 0, 1 or 2.");
        var budget = Enum.Parse<BudgetMode>(s.MakerBudget);
        int[]? custom = budget == BudgetMode.Custom ? s.MakerCustomBudget.Split(',').Select(int.Parse).ToArray() : null;
        if (custom is not null && (custom.Length != 3 || custom.Any(b => b < 0))) throw new ArgumentException("Custom budgets need three non-negative triangle limits.");
        if (s.MakerDetail < .1m || s.MakerDetail > 10 || s.MakerMinSize < 0) throw new ArgumentException("Check the simplification detail and minimum size.");
        return RunJob("Mesh maker", s, (p, ct) => LodMeshMaker.Run(new LodMakerRequest(inputs, output, s.MakerGroup, levels,
            (float)s.MakerDetail, (float)s.MakerMinSize, s.MakerRules, s.MakerOverwrite, budget, custom, RequestFactory.Optional(s.MakerPlugin)), p, ct),
            (r, folder, _) => { _resultViews["Mesh maker"].ItemsSource = r.Items; WriteResult(folder, r); Notice($"Wrote {r.MeshesWritten:N0} meshes. Review the report and generated assets."); });
    }
    private Task RunConvert()
    {
        var s = _settings.Snapshot(); var source = RequestFactory.RequiredPath(s.ConvertSource, "a source mod", s);
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
        var output = ToolOutput(s.ConvertOutput, s, [source]);
        return RunJob("Oldrim conversion", s, (p, ct) => OldrimLodConverter.Run(new ConvertRequest(source, output, s.ConvertAllMeshes, s.ConvertOverwrite), p, ct),
            (r, folder, _) => { _resultViews["Mesh maker"].ItemsSource = new[] { r }; WriteResult(folder, r); Notice($"Converted {r.Converted:N0} meshes; {r.AlreadySse:N0} already SSE, {r.Skipped:N0} skipped."); });
    }
    private Task RunAuthor(bool generate)
    {
        var s = _settings.Snapshot(); var game = RequestFactory.Game(s);
        if (string.IsNullOrWhiteSpace(s.AuthorPlugin)) throw new ArgumentException("Enter a plugin filename, or * for all trees.");
        if (s.AuthorMinSize < 0 || s.AuthorTreeHeight < 0) throw new ArgumentException("Minimum sizes cannot be negative.");
        string output = generate ? ToolOutput(s.AuthorOutput, s, []) : Path.Combine(
            RequestFactory.Optional(s.CacheFolder) is { } c ? RequestFactory.PathValue(c, s) : SettingsStore.CacheRoot, "analysis", Guid.NewGuid().ToString("N"));
        return RunJob(generate ? "Asset authoring" : "Asset analysis", s, (p, ct) => ModAuthorTool.Run(new AuthorRequest(game, s.AuthorPlugin.Trim(), output,
            generate && s.AuthorMeshes, generate && s.AuthorBillboards, generate && s.AuthorRules, (float)s.AuthorMinSize, (float)s.AuthorTreeHeight, generate && s.AuthorOverwrite,
            RequestFactory.Optional(s.DynDolodFolder) is { } d ? RequestFactory.PathValue(d, s) : null, TreeStandIns: generate && s.AuthorStandIns), p, ct),
            (r, folder, _) => { _resultViews["Mod author"].ItemsSource = r.Items; WriteResult(folder, r); Notice($"{r.Items.Count:N0} models inspected · {r.MeshesWritten:N0} meshes · {r.BillboardsWritten:N0} billboards. Full report: {r.ReportFile}"); });
    }
    private static void WriteResult<T>(string folder, T result) => File.WriteAllText(Path.Combine(folder, "report.json"), JsonSerializer.Serialize(result, SettingsStore.JsonOptions));
    private static string TextureSignature(DesktopSettings s) => string.Join('\n', s.TextureInputs, s.TextureAllMods, s.Instance, s.Profile, s.WinePrefix, s.GameFolder, s.ModsFolder, s.ProfilesFolder, s.OverwriteFolder);
    private Task ScanTextures()
    {
        var s = _settings.Snapshot();
        var folders = RequestFactory.Lines(s.TextureInputs).Select(p => RequestFactory.PathValue(p, s)).ToList();
        if (s.TextureAllMods)
        {
            if (s.SourceMode != "Mod Organizer 2") throw new ArgumentException("Enabled-mod discovery requires Mod Organizer 2 mode.");
            var g = RequestFactory.Game(s); var mo2 = Mo2Instance.Open(g.Mo2InstanceFolder!, g.Mo2Locations);
            folders.AddRange(mo2.OpenProfile(g.Mo2Profile).EnabledModFolders());
        }
        if (folders.Count == 0) throw new ArgumentException("Choose input texture folders or enable MO2 mod discovery.");
        foreach (var folder in folders) if (!Directory.Exists(folder)) throw new DirectoryNotFoundException(folder);
        _badTextures = []; _textureSourceSignature = "";
        return RunJob("Texture inspection", s, (p, ct) => TextureFixer.Scan(folders.Distinct(), p, ct), (r, folder, _) =>
        { _badTextures = r; _textures.ItemsSource = r; _textureSourceSignature = TextureSignature(s); WriteResult(folder, r); Notice($"{r.Count:N0} unsupported textures; {r.Count(t => t.Fixable):N0} BC6H textures can be repaired."); });
    }
    private Task RepairTextures()
    {
        var s = _settings.Snapshot();
        if (_textureSourceSignature != TextureSignature(s)) throw new ArgumentException("Scan textures again after changing the source settings.");
        if (!_badTextures.Any(t => t.Fixable)) throw new ArgumentException("No repairable BC6H textures were found. Run Scan textures first.");
        var output = ToolOutput(s.TextureOutput, s, _badTextures.Select(t => t.Root).Distinct());
        var items = _badTextures.ToArray();
        return RunJob("Texture repair", s, (p, ct) => TextureFixer.Fix(items, "", output, null, false, p, ct, NativeTextureConverter.Bc6ToBc7),
            (r, folder, _) => { WriteResult(folder, r); int failed = r.Count(t => t.Status.StartsWith("Failed"));
                Notice($"{r.Count - failed:N0} textures converted; {failed:N0} failed. Originals are unchanged. See the run report for details.");
                if (failed > 0) _status.Text = "Texture repair finished with errors"; });
    }
    private async Task LoadRules()
    {
        var path = await Pick(true, "Load LOD rules"); if (path is null) return;
        var parsed = RuleEntry.ParseFile(await File.ReadAllTextAsync(path), path, true);
        _rules.Clear(); foreach (var rule in parsed) _rules.Add(rule);
        Notice($"Loaded {parsed.Count} rules. Save a custom copy to use the edited rules.");
    }
    private async Task SaveRules()
    {
        if (_rules.Any(r => string.IsNullOrWhiteSpace(r.Mask))) throw new ArgumentException("Every rule needs a mesh mask or FormID.");
        var path = await SavePath("Save custom LOD rules", "AnvilLOD_CustomRules.ini", "ini"); if (path is null) return;
        await File.WriteAllTextAsync(path, RuleEntry.FormatFile(_rules)); _settings.CustomRulesFile = path;
        Notice("Saved and activated custom rules: " + path);
    }
    private async Task ImportSettings()
    {
        var path = await Pick(true, "Import AnvilLOD settings"); if (path is null) return;
        var settings = SettingsStore.Read(path);
        if (File.Exists(_store.FilePath)) File.Copy(_store.FilePath, _store.FilePath + ".before-import-" + DateTime.Now.ToString("yyyyMMddHHmmss"), true);
        ReplaceSettings(settings); Notice("Settings imported from " + path);
    }
    internal void ReplaceSettings(DesktopSettings settings)
    {
        if (_runner.IsRunning) throw new InvalidOperationException("Wait for the active job before importing settings.");
        GenerationOptions.Validate(settings);
        _settings = settings; _saveAllowed = true; AttachSettings(); BuildShell(); Save();
    }
    private async Task ExportSettings()
    {
        var path = await SavePath("Export AnvilLOD settings", "AnvilLOD-preset.json", "json"); if (path is null) return;
        SettingsStore.Write(path, _settings); Notice("Settings exported to " + path);
    }
    private void DetectInstall()
    {
        var found = InstallDiscovery.Find();
        if (found.Count == 0) { Notice("No Skyrim installation was found in Steam's libraries. Choose the game folder on Overview."); return; }
        var first = found[0]; _settings.GameFolder = first.GameFolder; _settings.WinePrefix = first.Prefix;
        if (first.PluginsFile is { } p) _settings.PluginsFile = p;
        Notice($"Detected {first.GameFolder}" + (found.Count > 1 ? $" ({found.Count} installations found; you can choose another on Overview.)" : ". Review the source paths on Overview."));
    }
}
