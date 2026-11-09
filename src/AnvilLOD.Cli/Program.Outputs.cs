using System.Text.Json;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Plugins;

namespace AnvilLOD.Cli;

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
        if (AnvilLOD.Core.Paths.HostPath.IsWithin(output, input)) throw new CliArgException("Save the report outside the folder being measured.");
        OutputManagement.ValidateReportPath(output, a.GetAll("settings") ?? []);
        var saved = Path.Combine(input, OutputCostReport.FileName);
        var settings = a.Get("settings") is { } preset
            ? OutputCostReport.GenerationSettings(AnvilLOD.Desktop.Services.RequestFactory.Scan(AnvilLOD.Desktop.Services.SettingsStore.Read(preset), false))
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
        var plan = GenerationScope.Review(LoadPipelineRequest(a), Cancellation);
        Console.WriteLine(plan.Summary()); WriteReport(a, plan); return 0;
    }
}
