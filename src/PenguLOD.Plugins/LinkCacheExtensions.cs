using System.Diagnostics.CodeAnalysis;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;

namespace PenguLOD.Plugins;

internal static class LinkCacheExtensions
{
    /// <summary>
    /// Resolves the winning record of any type. Used where a reference's base can be one of many record types and the
    /// type is checked afterwards. Mutagen marks the untyped lookup obsolete only because it is slower than a typed one.
    /// </summary>
    public static bool TryResolveAny(this ILinkCache cache, FormKey key, [NotNullWhen(true)] out IMajorRecordGetter? record)
    {
#pragma warning disable CS0618
        return cache.TryResolve(key, out record);
#pragma warning restore CS0618
    }
}
