using System.Text.Json;
using PenguLOD.Core.Pipeline;
using PenguLOD.Textures;

namespace PenguLOD.Desktop;

public sealed partial class MainWindow
{
    private Task CheckTextureCompute() => RunJob("Texture compute check",_settings.Snapshot(),(progress,ct)=>
    {
        ct.ThrowIfCancellationRequested();progress.Report("Checking the optional Vulkan device and texture workload...");
        return TextureCompute.Benchmark();
    },(result,folder,elapsed)=>
    {
        File.WriteAllText(Path.Combine(folder,"compute.json"),JsonSerializer.Serialize(result,PersistentPipeline.Json));
        Notice(result.Device+": "+result.Recommendation+$" CPU {result.CpuMilliseconds:F1} ms; Vulkan {result.VulkanMilliseconds:F1} ms.");
    });
}
