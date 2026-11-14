using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using PenguLOD.Core.World;

namespace PenguLOD.Plugins;

/// <summary>A worldspace in the load order, with what is needed to choose it for generation.</summary>
/// <param name="EditorId">The ID the Worldspaces settings and <c>--worldspace</c> take.</param>
/// <param name="Name">The name the game shows, when the worldspace has one.</param>
/// <param name="Plugin">The plugin that adds the worldspace.</param>
/// <param name="Parent">The worldspace this one takes its land from (walled cities), or null.</param>
/// <param name="HasLodSettings">True when <c>lodsettings\&lt;EditorId&gt;.lod</c> exists. Terrain and object LOD skip a worldspace without it.</param>
/// <param name="Cells">Cells along one side of the LOD grid, or null without readable LOD settings.</param>
public sealed record WorldspaceInfo(string EditorId, string? Name, string Plugin, string? Parent, bool HasLodSettings, int? Cells);

public static class WorldspaceCatalog
{
    /// <summary>Every worldspace in the load order: those with LOD settings first, largest first, then by ID.</summary>
    public static IReadOnlyList<WorldspaceInfo> List(GameContextOptions options, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        using var game = GameContext.Open(options with { ExtraLooseRoots = [.. options.ExtraLooseRoots ?? [], "lodsettings"] }, progress);
        ct.ThrowIfCancellationRequested();
        var assets = game.BuildAssets(p => p.StartsWith("lodsettings\\", StringComparison.Ordinal));
        var found = new List<WorldspaceInfo>();
        foreach (var world in game.LoadOrder.PriorityOrder.Worldspace().WinningOverrides())
        {
            ct.ThrowIfCancellationRequested();
            if (world.IsDeleted || world.EditorID is not { Length: > 0 } id) continue;
            bool settings = assets.TryOpen(LodSettings.RelativePathFor(id), out var stream);
            int? cells = null;
            if (settings)
                using (stream)
                {
                    var bytes = new byte[LodSettings.SizeInBytes];
                    // A short or damaged file still counts as present; generation reports what is wrong with it.
                    if (stream!.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false) == bytes.Length && LodSettings.Parse(bytes).Stride is > 0 and var stride) cells = stride;
                }
            string? parent = world.Parent?.Worldspace.TryResolve(game.LinkCache)?.EditorID;
            found.Add(new(id, string.IsNullOrWhiteSpace(world.Name?.String) ? null : world.Name!.String, world.FormKey.ModKey.FileName, parent, settings, cells));
        }
        return [.. found.OrderByDescending(w => w.HasLodSettings).ThenByDescending(w => w.Cells ?? 0).ThenBy(w => w.EditorId, StringComparer.OrdinalIgnoreCase)];
    }
}
