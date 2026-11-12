namespace PenguLOD.Cli;

public sealed class CliArgException(string message) : Exception(message);

public enum CliOptionKind { Flag, Value, Path }

/// <summary>
/// One command-line option. <paramref name="WithPreset"/> marks options that keep their meaning when the
/// generation settings come from --settings or --request: they select stages, control the run or name a report,
/// and do not change what is generated.
/// </summary>
public sealed record CliOption(string Name, CliOptionKind Kind, bool WithPreset = false);

/// <summary>"--key value" / "--flag" parser driven by <see cref="Options"/>. Keys may repeat.</summary>
public sealed class CliArgs
{
    private static CliOption F(string name, bool withPreset = false) => new(name, CliOptionKind.Flag, withPreset);
    private static CliOption V(string name, bool withPreset = false) => new(name, CliOptionKind.Value, withPreset);
    private static CliOption P(string name, bool withPreset = false) => new(name, CliOptionKind.Path, withPreset);

    /// <summary>Every option the CLI accepts. Usage text and docs/CLI.md are checked against this table by tests.</summary>
    public static readonly IReadOnlyList<CliOption> Options =
    [
        // Presets, saved jobs and run control.
        P("settings", true), P("request", true), V("stages", true), P("job-folder", true), P("cache-folder", true),
        V("threads", true), P("report", true), V("texture-compute", true), F("no-resume", true), F("no-preflight", true),
        F("verify-source-contents", true), F("expand-output-scope", true), F("deep-scan", true), F("verify-outputs", true),
        V("inspect-asset", true), P("wine-prefix", true), F("allow-software"),
        // Output management.
        P("input"), P("before"), P("after"), P("plan"), P("output"),
        // Game source.
        P("data"), P("plugins"), P("mo2"), V("profile"), P("game-ini"), P("mo2-game"), P("mo2-mods"), P("mo2-profiles"), P("mo2-overwrite"),
        // Scan and generate.
        V("worldspace"), F("keep-output"), P("dyndolod"), P("resources"), V("preset"), F("candles"), F("fxglow"), P("rules"),
        F("keep-buried"), F("keep-underwater"), F("include-disabled"), F("no-enable-parented"), F("no-child-worlds"), F("no-dynamic"),
        F("no-grid-objects"), F("glow-windows"), F("parent-copies"), F("no-match-textures"), F("no-grass"), F("water-standins"),
        F("seasons"), F("seasons-strict"), V("skse-dll"), V("esl-range"), F("native-grid-objects"), F("native-effects"), F("no-cs-grass"), F("no-animated-ships"),
        F("no-detailed-ships"), V("ice-inset"), F("no-mountain-fit"), V("mountain-inset"), V("object-inset"), V("snow-cover"), V("snow-shader"),
        V("map-objects"), V("grass-density"), V("grass-top"), V("grass-bottom"), V("tree-brightness"), V("object-brightness"),
        F("occlusion"), V("occlusion-clearance"), P("occlusion-folder"), F("object-atlas"), V("atlas-cell"),
        F("optimize-object-textures"), V("object-texture-size"), F("auto-objects"), V("object-near-budget"), V("object-middle-budget"),
        V("object-far-budget"), V("object-min-size"), V("object-detail"), F("large-refs"), F("large-refs-no-esl"),
        F("pbr-lod"), V("pbr-lod-brightness"), V("pbr-lod-size"), F("pbr-waterfalls"), F("underside"), V("underside-detail"),
        // Trees.
        F("no-trees"), F("render-trees"), F("tree-3d"), F("tree-3d-lod8"), F("tree-3d-crc-only"), F("tree-3d-dyndolod-first"),
        F("no-map-trees"), F("hybrid-trees"), V("tree-trunk-size"), V("tree-trunk-units"), V("tree-foliage-target"),
        V("tree-near-budget"), V("tree-far-budget"), V("tree-near-error"), V("tree-far-error"), F("no-native-trees"),
        // Authoring and repair tools.
        V("plugin"), P("author-out"), F("no-meshes"), F("no-billboards"), F("no-rules"), F("tree-stand-ins"), V("min-size"),
        V("min-tree-height"), F("overwrite"), V("budget"), V("group"), V("levels"), V("detail"), V("new-mod"),
        F("apply"), F("in-place"), P("backup"), P("texconv"), P("source"), F("all-meshes"),
    ];

    private static readonly Dictionary<string, CliOption> ByName = Options.ToDictionary(o => o.Name, StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, List<string>> _values = new(StringComparer.OrdinalIgnoreCase);

    public static CliArgs Parse(string[] args)
    {
        var r = new CliArgs();
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (!a.StartsWith("--")) throw new CliArgException($"Unexpected argument '{a}'.");
            var key = a[2..].ToLowerInvariant();
            if (!ByName.TryGetValue(key, out var option)) throw new CliArgException($"Unknown option '{a}'.");
            if (option.Kind == CliOptionKind.Flag) { r.Add(key, "true"); continue; }
            if (i + 1 >= args.Length) throw new CliArgException($"Option '{a}' needs a value.");
            r.Add(key, args[++i]);
        }
        return r;
    }

    private void Add(string k, string v)
    {
        if (!_values.TryGetValue(k, out var list)) _values[k] = list = [];
        list.Add(v);
    }

    public void ResolvePaths()
    {
        var prefix = Get("wine-prefix");
        foreach (var option in Options.Where(o => o.Kind == CliOptionKind.Path))
            if (_values.TryGetValue(option.Name, out var values))
                for (int i = 0; i < values.Count; i++) values[i] = PenguLOD.Core.Paths.HostPath.Resolve(values[i], winePrefix: prefix);
    }

    /// <summary>
    /// Refuses generation options next to --settings or --request: the preset or saved request defines the generation,
    /// so they would otherwise be ignored.
    /// </summary>
    public void RejectIgnoredWithPreset(params string[] alsoAllowed)
    {
        if (!Has("settings") && !Has("request")) return;
        var ignored = _values.Keys.Where(k => !ByName[k].WithPreset && !alsoAllowed.Contains(k, StringComparer.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal).Select(k => "--" + k).ToArray();
        if (ignored.Length > 0)
            throw new CliArgException($"{string.Join(", ", ignored)} cannot be combined with {(Has("settings") ? "--settings" : "--request")}: "
                + "the preset or saved request defines the generation. Change the preset in the desktop, or run without it.");
    }

    public bool Has(string k) => _values.ContainsKey(k);
    public string? Get(string k) => _values.TryGetValue(k, out var l) ? l[^1] : null;
    public IReadOnlyCollection<string>? GetAll(string k) => _values.TryGetValue(k, out var l) ? l : null;

    /// <summary>An invariant-culture number, or the default when the option is absent. Malformed or out-of-range values are errors.</summary>
    public float Number(string key, float fallback, float min, float max)
    {
        if (Get(key) is not { } text) return fallback;
        if (!float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value)
            || !float.IsFinite(value) || value < min || value > max)
            throw new CliArgException(FormattableString.Invariant($"--{key} must be a number from {min} to {max} (got '{text}')."));
        return value;
    }

    /// <summary>An invariant-culture whole number, or the default when the option is absent.</summary>
    public int Integer(string key, int fallback, int min = int.MinValue, int max = int.MaxValue)
    {
        if (Get(key) is not { } text) return fallback;
        if (!int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int value)
            || value < min || value > max)
            throw new CliArgException(min == int.MinValue && max == int.MaxValue
                ? $"--{key} must be a whole number (got '{text}')."
                : $"--{key} must be a whole number from {min} to {max} (got '{text}').");
        return value;
    }
}
