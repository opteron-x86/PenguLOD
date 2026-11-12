using PenguLOD.Core.Lod;

namespace PenguLOD.Meshes.Nif;

public sealed record GeometryCost(string Layer, long Triangles, long Vertices, int MeshGroups);

/// <summary>Counts reachable drawable geometry without decoding vertex buffers or textures.</summary>
public static class NifCostReader
{
    public static IReadOnlyList<GeometryCost> Read(byte[] bytes, string defaultLayer = LodLayers.Objects)
    {
        try { return ReadCore(bytes, defaultLayer); }
        catch (Exception e) when (e is OverflowException or ArgumentOutOfRangeException or IndexOutOfRangeException or EndOfStreamException)
        { throw new InvalidDataException("Malformed NIF: " + e.Message, e); }
    }

    private static IReadOnlyList<GeometryCost> ReadCore(byte[] bytes, string defaultLayer)
    {
        var nif = NifFile.Read(bytes);
        if (nif.BsVersion != NifFile.BsVersionSse) throw new NotSupportedException("Geometry costs require an SSE NIF.");
        var costs = new Dictionary<string, GeometryCost>(StringComparer.Ordinal);
        var visited = new HashSet<int>();
        var pending = new Stack<(int Index, string Layer, bool Hidden)>();
        foreach (int root in nif.Roots) pending.Push((root, defaultLayer, false));
        while (pending.TryPop(out var item))
        {
            if (item.Index == -1) continue;
            if (item.Index < 0 || item.Index >= nif.Blocks.Count) throw new InvalidDataException("Invalid scene reference.");
            if (!visited.Add(item.Index)) throw new InvalidDataException("Repeated or cyclic scene reference; drawable counts are ambiguous.");
            var block = nif.Blocks[item.Index];
            bool node = block.Type is "NiNode" or "BSMultiBoundNode" or "BSFadeNode";
            bool shape = block.Type is "BSTriShape" or "BSSubIndexTriShape";
            if (!node && !shape) throw new NotSupportedException("Unmeasured scene block: " + block.Type);
            var reader = nif.ReaderFor(block);
            string? name = nif.String(reader.I32());
            reader.Skip(checked((int)reader.U32() * 4)); // extra data
            reader.I32(); // controller
            uint flags = reader.U32();
            reader.Skip(56); // transform and collision link
            bool hidden = item.Hidden || (flags & 1) != 0;
            string layer = item.Layer;
            if (name is not null && (name.StartsWith(LodLayers.NodePrefix, StringComparison.Ordinal) || name.StartsWith("AL:", StringComparison.Ordinal)))
            {
                string candidate = name.Split(':')[1];
                layer = LodLayers.All.Contains(candidate) ? candidate : "other";
            }
            if (node)
            {
                uint count = reader.U32(); reader.Require(checked((int)count * 4));
                for (int i = 0; i < count; i++) pending.Push((reader.I32(), layer, hidden));
            }
            else
            {
                reader.Skip(28); // bound, skin, shader, alpha
                ulong descriptor = reader.U64();
                int triangles = reader.U16(), vertices = reader.U16();
                uint size = reader.U32();
                long required = (long)(descriptor & 15) * 4 * vertices + triangles * 6L;
                if (size < required || size > reader.Remaining) throw new InvalidDataException("Truncated geometry buffer.");
                if (hidden || triangles == 0 || vertices == 0) continue;
                var old = costs.GetValueOrDefault(layer) ?? new(layer, 0, 0, 0);
                costs[layer] = new(layer, old.Triangles + triangles, old.Vertices + vertices, old.MeshGroups + 1);
            }
        }
        return costs.Values.OrderBy(x => x.Layer, StringComparer.Ordinal).ToArray();
    }
}
