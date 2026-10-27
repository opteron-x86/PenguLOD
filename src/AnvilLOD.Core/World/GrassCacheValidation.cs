using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace AnvilLOD.Core.World;

public sealed record GrassCacheInfo(int Types, long Instances);

public static class GrassCacheValidation
{
    /// <summary>Validate the whole NGIO stream without allocating an object for every blade.</summary>
    public static GrassCacheInfo Inspect(Stream stream, CancellationToken ct = default)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var types = reader.ReadUInt32();
        if (types > 4096) throw new InvalidDataException("Invalid grass type count.");
        long instances = 0;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(256 * 1024);
        try
        {
            for (int type = 0; type < types; type++)
            {
                ct.ThrowIfCancellationRequested();
                uint length = reader.ReadUInt32();
                if (length is < 2 or > 1024) throw new InvalidDataException("Invalid grass model path length.");
                stream.ReadExactly(buffer.AsSpan(0, (int)length));
                if (buffer[length - 1] != 0) throw new InvalidDataException("Unterminated grass model path.");
                GamePath.ValidateRelative(Encoding.UTF8.GetString(buffer, 0, (int)length - 1));
                if (!float.IsFinite(reader.ReadSingle())) throw new InvalidDataException("Invalid grass wave period.");
                reader.ReadUInt32();
                for (int i = 0; i < 3; i++) if (reader.ReadByte() > 1) throw new InvalidDataException("Invalid grass flags.");
                uint chunks = reader.ReadUInt32();
                if (chunks > 1048576) throw new InvalidDataException("Invalid grass chunk count.");
                for (int chunk = 0; chunk < chunks; chunk++)
                {
                    ct.ThrowIfCancellationRequested();
                    for (int i = 0; i < 7; i++) if (!float.IsFinite(reader.ReadSingle())) throw new InvalidDataException("Invalid grass chunk bounds.");
                    uint count = reader.ReadUInt32(), halves = reader.ReadUInt32();
                    // Skyrim's reader has a fixed 256 KiB scratch buffer for each block.
                    if (halves != 16 || count > 8192) throw new InvalidDataException("Unsafe grass instance block size.");
                    int bytes = checked((int)count * 32);
                    stream.ReadExactly(buffer.AsSpan(0, bytes));
                    for (int offset = 0; offset < bytes; offset += 32)
                        for (int component = 0; component < 4; component++)
                            if (!Half.IsFinite(BinaryPrimitives.ReadHalfLittleEndian(buffer.AsSpan(offset + component * 2))))
                                throw new InvalidDataException("Invalid grass position or scale.");
                    instances += count;
                }
            }
            if (stream.ReadByte() != -1) throw new InvalidDataException("Unexpected trailing grass cache data.");
            return new((int)types, instances);
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
}
