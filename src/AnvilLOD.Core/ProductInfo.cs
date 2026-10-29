using System.Reflection;

namespace AnvilLOD.Core;

/// <summary>Application identity, independent of the game-side AnvilLOD protocol and filenames.</summary>
public static class ProductInfo
{
    public const string Name = "PenguLOD";
    public const string Repository = "https://github.com/opteron-x86/PenguLOD";
    public static string Platform => OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux" : "Desktop";
    public static string Version => typeof(ProductInfo).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
    public static string UpstreamVersion => typeof(ProductInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "AnvilLODUpstreamVersion").Value!;
    public static string Description => $"{Name} {Version} (AnvilLOD {UpstreamVersion})";
}
