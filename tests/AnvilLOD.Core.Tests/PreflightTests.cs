using AnvilLOD.Plugins;

namespace AnvilLOD.Core.Tests;

public class PreflightTests
{
    private static readonly string Mods = Path.Combine(Path.GetTempPath(), "Modlists", "Example", "mods");

    [Fact]
    public void A_ticked_output_mod_is_reported_by_name()
    {
        var issues = Preflight.Check(["Water", "ANvilLOD_output"], ["Skyrim.esm"], Mods, Path.Combine(Mods, "anvillod_OUTPUT"));
        var issue = Assert.Single(issues);
        Assert.Equal(Preflight.OutputModEnabled, issue.Code);
        Assert.Contains("anvillod_OUTPUT", issue.Message);
    }

    [Fact]
    public void An_unticked_output_mod_gives_no_issue()
    {
        Assert.Empty(Preflight.Check(["Water"], ["Skyrim.esm"], Mods, Path.Combine(Mods, "ANvilLOD_output")));
    }

    [Fact]
    public void An_output_folder_outside_the_mods_folder_is_not_a_mod()
    {
        var elsewhere = Path.Combine(Path.GetTempPath(), "LOD", "ANvilLOD_output");
        Assert.Null(Preflight.OutputModName(Mods, elsewhere));
        Assert.Null(Preflight.OutputModName(Mods, Path.Combine(Mods, "Nested", "ANvilLOD_output")));
        Assert.Equal("ANvilLOD_output", Preflight.OutputModName(Mods + Path.DirectorySeparatorChar,
            Path.Combine(Mods, "ANvilLOD_output") + Path.DirectorySeparatorChar));
        Assert.Empty(Preflight.Check(["ANvilLOD_output"], ["Skyrim.esm"], Mods, elsewhere));
    }

    [Fact]
    public void Enabled_DynDOLOD_plugins_are_reported()
    {
        var issues = Preflight.Check([], ["Skyrim.esm", "DynDOLOD.esm", "DynDOLOD.esp", "DynDOLOD_Foo.txt"], Mods, null);
        var issue = Assert.Single(issues);
        Assert.Equal(Preflight.DynDolodEnabled, issue.Code);
        Assert.Contains("DynDOLOD.esm, DynDOLOD.esp", issue.Message);
    }

    [Fact]
    public void A_game_source_other_than_MO2_is_not_checked()
    {
        Assert.Empty(Preflight.Check(new GameContextOptions(DataFolder: "C:\\Game\\Data"), "C:\\x"));
    }
}
