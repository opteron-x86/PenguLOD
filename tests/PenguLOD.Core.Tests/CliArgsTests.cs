using System.Text.RegularExpressions;
using PenguLOD.Cli;
using PenguLOD.Desktop.Models;
using PenguLOD.Desktop.Services;
using PenguLOD.Testing;
using Xunit;

namespace PenguLOD.Core.Tests;

[Collection("CLI")]
public class CliArgsTests
{
    private static string RepoFile(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "PenguLOD.slnx"))) return Path.Combine(dir.FullName, relative);
        throw new FileNotFoundException("Repository root not found.");
    }

    private static string[] Mentioned(string text) => Regex.Matches(text, @"(?<![\w-])--([a-z][a-z0-9-]*[a-z0-9])")
        .Select(m => m.Groups[1].Value).Where(n => n is not ("help" or "version")).Distinct().Order().ToArray();

    [Fact]
    public void EveryOptionInUsageAndDocsIsAccepted()
    {
        var known = CliArgs.Options.Select(o => o.Name).ToHashSet();
        var usage = Mentioned(Program.Usage).Where(n => !known.Contains(n)).ToArray();
        Assert.True(usage.Length == 0, "Usage mentions unknown options: " + string.Join(", ", usage));
        var docs = Mentioned(File.ReadAllText(RepoFile("docs/CLI.md"))).Where(n => !known.Contains(n)).ToArray();
        Assert.True(docs.Length == 0, "docs/CLI.md mentions unknown options: " + string.Join(", ", docs));
    }

    [Fact]
    public void EveryAcceptedOptionIsDocumentedInUsage()
    {
        var usage = Mentioned(Program.Usage).ToHashSet();
        var missing = CliArgs.Options.Select(o => o.Name).Where(n => !usage.Contains(n)).ToArray();
        Assert.True(missing.Length == 0, "Options missing from usage: " + string.Join(", ", missing));
        Assert.Equal(CliArgs.Options.Count, CliArgs.Options.Select(o => o.Name).Distinct().Count());
    }

    [Fact]
    public void EveryOptionParsesWithItsKind()
    {
        foreach (var option in CliArgs.Options)
        {
            var args = option.Kind == CliOptionKind.Flag ? new[] { "--" + option.Name } : ["--" + option.Name, "value"];
            Assert.True(CliArgs.Parse(args).Has(option.Name), option.Name);
        }
    }

    [Theory]
    [InlineData("--esl-range", "2048")]
    [InlineData("--ice-inset", "4")]
    [InlineData("--mountain-inset", "8")]
    [InlineData("--object-inset", "2")]
    [InlineData("--map-objects", "rules")]
    [InlineData("--snow-cover", "bright")]
    [InlineData("--snow-shader", "plain")]
    [InlineData("--object-texture-size", "512")]
    public void PreviouslyRejectedOptionsParse(string option, string value) =>
        Assert.Equal(value, CliArgs.Parse([option, value]).Get(option[2..]));

    [Fact]
    public void MalformedArgumentsAreRejected()
    {
        Assert.Throws<CliArgException>(() => CliArgs.Parse(["--grass-density"]));
        Assert.Throws<CliArgException>(() => CliArgs.Parse(["--not-an-option"]));
        Assert.Throws<CliArgException>(() => CliArgs.Parse(["stray"]));
        Assert.True(CliArgs.Parse(["--optimize-object-textures"]).Has("optimize-object-textures"));
    }

    [Theory]
    [InlineData("15%")]
    [InlineData("0")]
    [InlineData("101")]
    [InlineData("NaN")]
    public void NumbersOutsideTheirRangeAreErrors(string value)
    {
        var a = CliArgs.Parse(["--grass-density", value]);
        Assert.Throws<CliArgException>(() => a.Number("grass-density", 8, 1, 100));
    }

    [Fact]
    public void NumbersUseInvariantCultureAndDefaults()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal(12.5f, CliArgs.Parse(["--grass-density", "12.5"]).Number("grass-density", 8, 1, 100));
        }
        finally { Thread.CurrentThread.CurrentCulture = previous; }
        Assert.Equal(8f, CliArgs.Parse([]).Number("grass-density", 8, 1, 100));
        Assert.Throws<CliArgException>(() => CliArgs.Parse(["--atlas-cell", "big"]).Integer("atlas-cell", 256));
    }

    [Fact]
    public void ExitCodesForHelpVersionAndBadInput()
    {
        Assert.Equal(0, Program.Main(["--version"]));
        Assert.Equal(0, Program.Main(["--help"]));
        Assert.Equal(1, Program.Main([]));
        Assert.Equal(2, Program.Main(["no-such-command"]));
        Assert.Equal(2, Program.Main(["scan", "--no-such-option"]));
        Assert.Equal(2, Program.Main(["scan", "--grass-density", "15%"]));
        Assert.Equal(2, Program.Main(["scan", "--snow-shader", "shiny"]));
        Assert.Equal(2, Program.Main(["scan", "--hybrid-trees"]));
        Assert.Equal(2, Program.Main(["scan", "--underside", "--underside-detail", "4"]));
    }

    [Fact]
    public void GenerationOptionsCannotBeCombinedWithAPreset()
    {
        using var f = new TextureFixture();
        string output = Path.Combine(f.Root, "elsewhere");
        string preset = Path.Combine(f.Root, "preset.json");
        SettingsStore.Write(preset, new DesktopSettings { Instance = "" });
        var a = CliArgs.Parse(["--settings", preset, "--output", output, "--worldspace", "Tamriel", "--threads", "2"]);
        var e = Assert.Throws<CliArgException>(() => a.RejectIgnoredWithPreset());
        Assert.Contains("--output", e.Message); Assert.Contains("--worldspace", e.Message); Assert.DoesNotContain("--threads", e.Message);
        CliArgs.Parse(["--settings", preset, "--output", output]).RejectIgnoredWithPreset("output");
        CliArgs.Parse(["--settings", preset, "--threads", "2", "--stages", "lod", "--no-preflight"]).RejectIgnoredWithPreset();
        CliArgs.Parse(["--output", output, "--worldspace", "Tamriel"]).RejectIgnoredWithPreset();

        foreach (var command in new[] { "generate", "scan", "pipeline", "plan-generation" })
            Assert.Equal(2, Program.Main([command, "--settings", preset, "--output", output]));
        Assert.False(Directory.Exists(output));
    }
}
