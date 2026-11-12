using System.Text.Json;
using PenguLOD.Core.Pipeline;
using PenguLOD.Plugins;

namespace PenguLOD.Cli;

public static partial class Program
{
    private static string Required(CliArgs a, string key) => a.Get(key) ?? throw new CliArgException("--" + key + " is required.");
    private static void WriteReport(CliArgs a, object report)
    {
        if (a.Get("report") is not { } file) return;
        OutputManagement.ValidateReportPath(file, new[] { "input", "before", "after", "settings", "request", "output" }.SelectMany(k => a.GetAll(k) ?? []));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        File.WriteAllText(file, JsonSerializer.Serialize(report, PersistentPipeline.Json));
    }
    private static int RunCostReport(CliArgs a)
    {
        string input = Required(a, "input"), output = Required(a, "report");
        // Analysis must not modify the output being measured.
        if (PenguLOD.Core.Paths.HostPath.IsWithin(output, input)) throw new CliArgException("Save the report outside the folder being measured.");
        OutputManagement.ValidateReportPath(output, a.GetAll("settings") ?? []);
        var saved = Path.Combine(input, OutputCostReport.FileName);
        var settings = a.Get("settings") is { } preset
            ? OutputCostReport.GenerationSettings(PenguLOD.Desktop.Services.RequestFactory.Scan(PenguLOD.Desktop.Services.SettingsStore.Read(preset), false))
            : File.Exists(saved) ? OutputCostReport.Read(saved).Settings : null;
        var report = OutputCostReport.Analyze(input, settings, new SyncProgress(Console.WriteLine), Cancellation);
        report.Write(output); Console.WriteLine(report.Summary()); return 0;
    }
    private static int CompareCosts(CliArgs a)
    {
        var comparison = OutputCostReport.Compare(OutputCostReport.Read(Required(a, "before")), OutputCostReport.Read(Required(a, "after")));
        Console.WriteLine(comparison.Summary()); WriteReport(a, comparison); return 0;
    }
    private static int VerifyOutput(CliArgs a)
    {
        var report = OutputManagement.Verify(a.GetAll("input") ?? throw new CliArgException("--input is required."), Cancellation);
        Console.WriteLine(report.Summary()); WriteReport(a, report); return report.Valid ? 0 : 1;
    }
    private static int PlanUpdate(CliArgs a)
    {
        var plan = OutputManagement.Plan(a.GetAll("input") ?? throw new CliArgException("--input is required."), Required(a, "output"), Cancellation);
        plan.Write(Required(a, "report")); Console.WriteLine(plan.Summary()); return plan.CanPublish ? 0 : 1;
    }
    private static int PublishUpdate(CliArgs a)
    {
        var plan = JsonSerializer.Deserialize<OutputUpdatePlan>(File.ReadAllText(Required(a, "plan")), PersistentPipeline.Json)
            ?? throw new InvalidDataException("Empty output plan.");
        string? backup = OutputManagement.Publish(plan, new SyncProgress(Console.WriteLine), Cancellation);
        Console.WriteLine("Published package. Previous output: " + (backup ?? "none")); return 0;
    }
    private static int PlanGeneration(CliArgs a)
    {
        if (a.Has("request") == a.Has("settings")) throw new CliArgException("Choose exactly one of --request or --settings.");
        a.RejectIgnoredWithPreset();
        object report; string summary; IEnumerable<string> outputs;
        if (a.Get("settings") is { } preset)
        {
            var stages = PenguLOD.Desktop.Services.RequestFactory.OutputScope(LoadStageSettings(preset, SelectedStages(a)));
            var plan = GenerationScope.ReviewOutputs(stages, Cancellation);
            report = plan; summary = plan.Summary(); outputs = stages.Select(s => s.OutputFolder);
        }
        else
        {
            var request = LoadPipelineRequest(a);
            var plan = GenerationScope.Review(request, Cancellation);
            report = plan; summary = plan.Summary();
            outputs = new[] { request.Grass?.OutputFolder, request.Textures?.OutputFolder, request.Terrain?.OutputFolder, request.Lod?.OutputFolder }.OfType<string>();
        }
        if (a.Get("report") is { } destination) OutputManagement.ValidateReportPath(destination, outputs);
        Console.WriteLine(summary); WriteReport(a, report); return 0;
    }
}
