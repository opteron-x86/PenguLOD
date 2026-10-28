using AnvilLOD.Desktop.Models;
using AnvilLOD.Desktop.Services;
using AnvilLOD.Plugins;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace AnvilLOD.Desktop;

public sealed partial class MainWindow
{
    private readonly TextBlock _diagnosticSummary = Text("Run diagnostics for the stages selected on Pipeline.", true);
    private readonly DataGrid _diagnosticIssues = Table(("Severity", "Severity"), ("Finding", "Message"), ("Next step", "Action"), ("Asset", "Asset"), ("Winner", "Source"));
    private readonly DataGrid _diagnosticEstimates = Table(("Metric", "Name"), ("Value", "Value"), ("Unit", "Unit"), ("Basis", "Basis"));
    private DiagnosticReport? _diagnostics;
    private string? _diagnosticFile;
    private Control DiagnosticsPage() => Scroll(Stack(16,
        Heading("Diagnostics", "Check the selected pipeline, runtime requirements, reusable outputs and model coverage."),
        Card("CHECKS", Stack(12,
            Check("Scan model and billboard coverage", nameof(DesktopSettings.DiagnosticDeepScan)),
            Check("Verify generated file contents", nameof(DesktopSettings.DiagnosticVerifyFiles)),
            Text("Coverage opens the selected worldspaces and checks up to 1,000 distinct models. Content verification reads every recorded generated file and can take several minutes on large outputs.", true),
            Field("Inspect asset precedence (optional)", nameof(DesktopSettings.DiagnosticAsset), "meshes\\landscape\\trees\\tree.nif"),
            Row(Primary("Run diagnostics", RunDiagnostics), Button("Export diagnostic report", ExportDiagnostics), Button("Open diagnostic report", () => Open(_diagnosticFile))))),
        Card("RESULTS", Stack(12, _diagnosticSummary, _diagnosticIssues)),
        Card("COUNTS & ESTIMATES", Stack(12, Text("Counts and geometry estimates do not predict elapsed time. Preparation and checks are measured separately from generation.", true), _diagnosticEstimates))));

    internal Task RunDiagnostics()
    {
        var s = _settings.Snapshot();
        return RunJob("Diagnostics", s, (progress, ct) => PipelineDiagnostics.Run(RequestFactory.Pipeline(s), s.DiagnosticDeepScan,
            s.DiagnosticVerifyFiles, RequestFactory.Optional(s.DiagnosticAsset), progress, ct), (report, folder, elapsed) =>
        {
            string path = Path.Combine(folder, "diagnostics.json"); report.Write(path); ShowDiagnostics(report, path);
            _status.Text = report.CanGenerate ? "Diagnostics complete" : "Diagnostics found blocking issues";
            Notice("Review the findings. The diagnostic JSON contains metadata and replaces known personal paths; it does not include game assets or the full run log.");
        });
    }
    private void ShowDiagnostics(DiagnosticReport report, string path)
    {
        _diagnostics = report; _diagnosticFile = path; _diagnosticIssues.ItemsSource = report.Issues; _diagnosticEstimates.ItemsSource = report.Estimates;
        _diagnosticSummary.Text = $"{report.Issues.Count(i => i.Severity == "Error")} errors · {report.Issues.Count(i => i.Severity == "Warning")} warnings · {report.Plugins:N0} plugins · {report.MeasuredSeconds:F1}s checking.\nRuntime: {report.GameVersion ?? "not detected"}";
        if (report.TreeCoverage.Count > 0) _treeCoverage.ItemsSource = report.TreeCoverage;
    }
    private async Task ExportDiagnostics()
    {
        if (_diagnostics is null) { Notice("Run diagnostics before exporting a report."); return; }
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export diagnostics", SuggestedFileName = "PenguLOD-diagnostics.json", DefaultExtension = "json" });
        if (file?.TryGetLocalPath() is { } path) { _diagnostics.Write(path); Notice("Diagnostic report exported: " + path); }
    }
}
