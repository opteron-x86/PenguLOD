using System.Text;

namespace AnvilLOD.Meshes.Nif;

/// <summary>
/// Minimal reader for Skyrim SE NIFs (20.2.0.7, user version 12, BS version 100).
/// Parses the header and block table only; blocks are decoded on demand by
/// <see cref="NifGeometryReader"/>. Unknown blocks are skipped using the size table,
/// so any valid SSE NIF can be opened even if it contains block types we don't use.
/// </summary>
public sealed class NifFile
{
    public const uint Version = 0x14020007;
    public const uint UserVersion = 12;
    public const uint BsVersionSse = 100;
    public const uint BsVersionLe = 83;

    public byte[] Data { get; }
    public uint BsVersion { get; }
    public IReadOnlyList<string> Strings { get; }
    public IReadOnlyList<NifBlock> Blocks { get; }
    public IReadOnlyList<int> Roots { get; }

    private NifFile(byte[] data, uint bsVersion, List<string> strings, List<NifBlock> blocks, List<int> roots)
    {
        Data = data;
        BsVersion = bsVersion;
        Strings = strings;
        Blocks = blocks;
        Roots = roots;
    }

    public static NifFile Read(byte[] data)
    {
        var r = new NifSpanReader(data, 0);

        int nl = Array.IndexOf(data, (byte)'\n');
        if (nl < 0 || nl > 64) throw new InvalidDataException("Not a NIF file (no header line).");
        var headerLine = Encoding.ASCII.GetString(data, 0, nl);
        if (!headerLine.StartsWith("Gamebryo File Format", StringComparison.Ordinal))
            throw new InvalidDataException($"Not a Gamebryo NIF: \"{headerLine}\".");
        r.Position = nl + 1;

        uint version = r.U32();
        if (version != Version) throw new NotSupportedException($"NIF version 0x{version:X8} is not supported (need 20.2.0.7).");
        byte endian = r.U8();
        if (endian != 1) throw new NotSupportedException("Big-endian NIFs are not supported.");
        uint userVersion = r.U32();
        uint numBlocks = r.U32();
        if (userVersion != UserVersion) throw new NotSupportedException($"NIF user version {userVersion} is not supported (need 12).");
        uint bsVersion = r.U32();
        // Skyrim SE (100) and Skyrim LE / Oldrim (83): the header, nodes and shader blocks are laid out the same; only the geometry differs
        // (NiTriShape + NiTriShapeData instead of BSTriShape vertex buffers), which NifGeometryReader reads for both.
        if (bsVersion != BsVersionSse && bsVersion != BsVersionLe)
            throw new NotSupportedException($"NIF BS version {bsVersion} is not supported (need 100 / Skyrim SE or 83 / Skyrim LE).");

        r.ExportString(); // author
        r.ExportString(); // process script
        r.ExportString(); // export script

        int numTypes = r.U16();
        r.Require(numTypes * 4);
        var types = new string[numTypes];
        for (int i = 0; i < numTypes; i++) types[i] = r.SizedString();

        r.Require(checked((int)numBlocks * 6));
        var typeIndex = new ushort[numBlocks];
        for (int i = 0; i < numBlocks; i++) typeIndex[i] = r.U16();
        var sizes = new uint[numBlocks];
        for (int i = 0; i < numBlocks; i++) sizes[i] = r.U32();

        uint numStrings = r.U32();
        r.U32(); // max string length
        r.Require(checked((int)numStrings * 4));
        var strings = new List<string>((int)numStrings);
        for (int i = 0; i < numStrings; i++) strings.Add(r.SizedString());

        uint numGroups = r.U32();
        r.Skip(checked((int)numGroups * 4));

        var blocks = new List<NifBlock>((int)numBlocks);
        int pos = r.Position;
        for (int i = 0; i < numBlocks; i++)
        {
            int type = typeIndex[i] & 0x7FFF;
            if (type >= types.Length || sizes[i] > data.Length - pos) throw new InvalidDataException("Invalid NIF block type or size.");
            blocks.Add(new NifBlock(i, types[type], pos, (int)sizes[i]));
            pos = checked(pos + (int)sizes[i]);
        }
        if (pos > data.Length) throw new InvalidDataException("NIF is truncated.");

        r.Position = pos;
        var roots = new List<int>();
        if (r.Remaining >= 4)
        {
            uint numRoots = r.U32();
            r.Require(checked((int)numRoots * 4));
            for (int i = 0; i < numRoots; i++) roots.Add(r.I32());
        }
        if (roots.Count == 0 && blocks.Count > 0) roots.Add(0);

        return new NifFile(data, bsVersion, strings, blocks, roots);
    }

    public string? String(int index) => index >= 0 && index < Strings.Count ? Strings[index] : null;

    public NifSpanReader ReaderFor(NifBlock b) => new(Data, b.Offset, b.Size);
}

public readonly record struct NifBlock(int Index, string Type, int Offset, int Size);

/// <summary>Little-endian cursor over a byte array.</summary>
public struct NifSpanReader
{
    private readonly byte[] _d;
    private readonly int _start, _end;
    private int _position;
    public int Position
    {
        readonly get => _position;
        set
        {
            if (value < _start || value > _end) throw new InvalidDataException("NIF block is truncated or has an invalid offset.");
            _position = value;
        }
    }

    public NifSpanReader(byte[] data, int position, int? length = null)
    {
        _d = data;
        _start = _position = position;
        _end = length is { } size ? checked(position + size) : data.Length;
        if (position < 0 || _end < position || _end > data.Length) throw new InvalidDataException("NIF block is outside the file.");
    }

    public readonly int Remaining => _end - Position;
    public readonly void Require(int count)
    {
        if (count < 0 || count > Remaining) throw new InvalidDataException("NIF block is truncated or has an invalid count.");
    }

    public byte U8() { Require(1); return _d[_position++]; }
    public ushort U16() { Require(2); var v = BitConverter.ToUInt16(_d, Position); _position += 2; return v; }
    public uint U32() { Require(4); var v = BitConverter.ToUInt32(_d, Position); _position += 4; return v; }
    public int I32() { Require(4); var v = BitConverter.ToInt32(_d, Position); _position += 4; return v; }
    public ulong U64() { Require(8); var v = BitConverter.ToUInt64(_d, Position); _position += 8; return v; }
    public float F32() { Require(4); var v = BitConverter.ToSingle(_d, Position); _position += 4; return v; }
    public Half F16() { Require(2); var v = BitConverter.ToHalf(_d, Position); _position += 2; return v; }

    public string SizedString()
    {
        int n = (int)U32();
        Require(n);
        var s = Encoding.Latin1.GetString(_d, Position, n);
        Position += n;
        return s;
    }

    public string ExportString()
    {
        int n = U8();
        Require(n);
        var s = Encoding.Latin1.GetString(_d, Position, n).TrimEnd('\0');
        Position += n;
        return s;
    }

    public void Skip(int n) { Require(n); _position += n; }
}
