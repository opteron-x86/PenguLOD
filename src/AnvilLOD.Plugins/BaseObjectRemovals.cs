using System.Globalization;
using System.Text.RegularExpressions;
using AnvilLOD.Core.World;
using AnvilLOD.Core.Pipeline;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace AnvilLOD.Plugins;

public sealed record BaseObjectRemoval(string Reference, string Base, string Worldspace, string File, int Line);
public sealed record BaseObjectSwapDiagnostic(string File, int Line, string Reason);
public sealed record BaseObjectSwapReport(bool RuntimeInstalled, int ConfigFiles, int RemovalRules,
    IReadOnlyList<BaseObjectRemoval> RemovedReferences, IReadOnlyList<BaseObjectSwapDiagnostic> Diagnostics);

/// <summary>
/// Applies the deterministic invisible-marker subset of Base Object Swapper. A rule we cannot
/// evaluate blocks an earlier removal for that source; it must never become an unconditional removal.
/// Files come from the winning virtual Data layer, in BOS's case-sensitive filename order.
/// </summary>
internal sealed partial class BaseObjectRemovals
{
    private sealed record Rule(bool Remove, string File, int Line);
    private static readonly FormKey XMarker = new(ModKey.FromNameAndExtension("Skyrim.esm"), 0x3B);
    private readonly Dictionary<FormKey, Rule> _references = [], _forms = [];
    private readonly HashSet<FormKey> _conditionalForms = [];
    private readonly List<BaseObjectRemoval> _removed = [];
    private readonly List<BaseObjectSwapDiagnostic> _diagnostics = [];
    private readonly HashSet<FormKey> _excluded = [];
    private bool _runtime;
    private int _files;

    public IReadOnlySet<FormKey> Excluded => _excluded;
    public BaseObjectSwapReport Report => new(_runtime, _files,
        _references.Values.Count(r => r.Remove) + _forms.Count(p => p.Value.Remove && !_conditionalForms.Contains(p.Key)),
        _removed.ToArray(), _diagnostics.ToArray());

    public static BaseObjectRemovals Load(GameContext game, IAssetSource assets, CancellationToken ct, IProgress<string>? progress = null)
    {
        var result = new BaseObjectRemovals();
        var files = game.RootFiles().Where(f => f.Name.EndsWith("_SWAP.ini", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => Path.GetFileName(f.FullPath), StringComparer.Ordinal).ToArray();
        result._files = files.Length;
        result._runtime = assets.Exists(@"skse\plugins\po3_BaseObjectSwapper.dll");
        if (!result._runtime || files.Length == 0) return result;
        progress?.Report($"Reading {files.Length:N0} Base Object Swapper configuration files...");
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var runtimeIds = RuntimeFormIds.FromLoadOrder(game.LoadOrder);
        var resolved = new Dictionary<(string, Type), IMajorRecordGetter?>();
        IMajorRecordGetter? Resolve<T>(string identifier) where T : class, IMajorRecordGetter
        {
            identifier = identifier.Trim();
            if (resolved.TryGetValue((identifier, typeof(T)), out var cached)) return cached;
            T? record = null;
            int tilde = identifier.IndexOf('~');
            if (tilde > 0)
            {
                if (Hex(identifier[..tilde], out uint id) && id <= 0xFFFFFF
                    && SeasonalLod.PluginForm(identifier[(tilde + 1)..].Trim(), id) is { } key)
                    game.LinkCache.TryResolve<T>(key, out record);
            }
            else if (Hex(identifier, out uint id))
            {
                if (runtimeIds.ToFormKey(id) is { } key) game.LinkCache.TryResolve<T>(key, out record);
            }
            else game.LinkCache.TryResolve<T>(identifier, out record);
            resolved[(identifier, typeof(T))] = record;
            return record;
        }
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            string name = Path.GetFileName(file.FullPath);
            if (timer.Elapsed.TotalSeconds >= 1)
            { progress?.Report($"Base Object Swapper: reading {name}"); timer.Restart(); }
            string[] lines;
            try { lines = File.ReadAllLines(file.FullPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { throw new IOException($"Cannot read Base Object Swapper configuration {name}; LOD exclusions cannot be determined.", ex); }
            string section = "";
            for (int i = 0; i < lines.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] is ';' or '#') continue;
                if (line[0] == '[' && line.EndsWith(']')) { section = line[1..^1]; continue; }
                if (section.Length == 0) continue;
                void Diagnostic(string reason) => result._diagnostics.Add(new(name, i + 1, reason));
                bool conditional = section.Contains('|'), forms = section.Split('|')[0] == "Forms";
                // Conditional sections other than Forms are property overrides, not reference swaps.
                if (section is "Properties" or "Transforms" || (conditional && !forms))
                { Diagnostic("Property overrides are not applied to LOD."); continue; }
                var fields = line.Split('|');
                if (fields.Length < 2) { Diagnostic("Malformed swap entry."); continue; }
                var sources = fields[0].Split(',').Select(id => forms ? Resolve<IPlaceableObjectGetter>(id) : Resolve<IPlacedObjectGetter>(id))
                    .OfType<IMajorRecordGetter>().Select(r => r.FormKey).Distinct().ToArray();
                if (sources.Length == 0) { Diagnostic("Source is unresolved in the selected load order."); continue; }
                // BOS ignores unresolved targets. Keep valid entries from a target list, as BOS does.
                var targets = fields[1].Split(',').Select(Resolve<IPlaceableObjectGetter>).OfType<IPlaceableObjectGetter>()
                    .Where(r => !r.IsDeleted).Select(r => r.FormKey).Distinct().ToArray();
                if (targets.Length == 0) { Diagnostic("Replacement is unresolved or is not a placeable object."); continue; }
                if (conditional)
                {
                    result._conditionalForms.UnionWith(sources);
                    Diagnostic("Conditional base swaps are not applied; they block unconditional base removals.");
                    continue;
                }
                bool certain = fields.Length < 4 || CertainChance(fields[3]);
                bool remove = targets.Length == 1 && targets[0] == XMarker && certain;
                foreach (var source in sources)
                {
                    if (targets.Length == 1 && source == targets[0]
                        && (fields.Length < 3 || !PropertyFunction().IsMatch(fields[2]))) continue;
                    (forms ? result._forms : result._references)[source] = new(remove, name, i + 1);
                }
                if (!remove) Diagnostic(!certain ? "Chance-based swap is not evaluated."
                    : targets.Length > 1 ? "Random replacement choice is not evaluated." : "Visible model replacement is not applied to LOD.");
            }
        }
        return result;
    }

    public bool Exclude(IPlacedObjectGetter reference, string worldspace)
    {
        if (reference.Base.FormKeyNullable is not { } baseKey) return false;
        // A reference-specific swap takes precedence over conditional and unconditional base swaps.
        if (!_references.TryGetValue(reference.FormKey, out var rule))
        {
            if (_conditionalForms.Contains(baseKey) || !_forms.TryGetValue(baseKey, out rule)) return false;
        }
        if (!rule.Remove) return false;
        if (_excluded.Add(reference.FormKey))
            _removed.Add(new(reference.FormKey.ToString(), baseKey.ToString(), worldspace, rule.File, rule.Line));
        return true;
    }

    private static bool CertainChance(string traits)
    {
        // Legacy removers put flag(...) here. BOS treats this field as chance, not record flags.
        if (!traits.Contains("chance", StringComparison.Ordinal)) return true;
        var match = Chance().Match(traits);
        return match.Success && float.TryParse(match.Groups[1].Value, NumberStyles.Float,
            CultureInfo.InvariantCulture, out float value) && float.IsFinite(value) && value >= 100;
    }
    private static bool Hex(string text, out uint id) => uint.TryParse(text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        ? text[2..] : text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out id);

    [GeneratedRegex(@"chance[SLR]?\(\s*([^,)]+)")]
    private static partial Regex Chance();
    [GeneratedRegex(@"(?:pos[AR]?|rot[AR]?|scaleA?|flagsC?)\(")]
    private static partial Regex PropertyFunction();
}
