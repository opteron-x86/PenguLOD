using System.Buffers;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using AnvilLOD.Core.Paths;

namespace AnvilLOD.Core.Pipeline;

/// <summary>Hashes recorded from successful writes to one private staging directory, valid only during that build.
/// Published output and resumed jobs always read and hash their actual files.</summary>
public sealed class WrittenOutputHashes : IDisposable
{
    private sealed record Receipt(long Length, long Modified, string Hash);
    private static readonly AsyncLocal<WrittenOutputHashes?> Current = new();
    private readonly ConcurrentDictionary<string, Receipt> _files = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly WrittenOutputHashes? _previous;
    private readonly string _root;
    private bool _disposed;

    public WrittenOutputHashes(string root)
    {
        _root = HostPath.Canonical(root); _previous = Current.Value; Current.Value = this;
    }

    public static void Record(string path, ReadOnlySpan<byte> bytes)
    {
        if (Current.Value is not { _disposed: false } scope || !HostPath.IsWithin(path, scope._root)) return;
        scope.Remember(path, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    private void Remember(string path, long length, string hash)
    {
        var file = new FileInfo(path);
        if (file.LinkTarget is not null || file.Length != length) throw new IOException("Output changed while being written: " + path);
        _files[Path.GetFullPath(path)] = new(length, file.LastWriteTimeUtc.Ticks, hash);
    }

    public static bool TryGet(string path, out string hash)
    {
        hash = "";
        if (Current.Value is not { _disposed: false } scope || !scope._files.TryGetValue(Path.GetFullPath(path), out var receipt)) return false;
        var file = new FileInfo(path);
        if (!file.Exists || file.LinkTarget is not null || file.Length != receipt.Length || file.LastWriteTimeUtc.Ticks != receipt.Modified) return false;
        hash = receipt.Hash; return true;
    }

    /// <summary>Copy independently, with cancellation, hashing the same bytes that are written instead of reading them again.</summary>
    public static void Copy(string source, string destination, CancellationToken ct = default)
    {
        var before = new FileInfo(source);
        if (before.LinkTarget is not null) throw new IOException("Cannot seed output from a symbolic link: " + source);
        long length = before.Length, modified = before.LastWriteTimeUtc.Ticks;
        var scope = Current.Value;
        bool record = scope is { _disposed: false } && HostPath.IsWithin(destination, scope._root);
        using var hash = record ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
        long copied = 0;
        try
        {
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, FileOptions.SequentialScan);
            using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 1, FileOptions.SequentialScan);
            int count;
            while ((count = input.Read(buffer)) != 0)
            {
                ct.ThrowIfCancellationRequested(); RunLimits.Check();
                output.Write(buffer, 0, count); hash?.AppendData(buffer, 0, count); copied += count;
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
        ct.ThrowIfCancellationRequested(); before.Refresh();
        if (copied != length || before.Length != length || before.LastWriteTimeUtc.Ticks != modified)
            throw new IOException("Previous output changed while being copied: " + source);
        File.SetLastWriteTimeUtc(destination, new DateTime(modified, DateTimeKind.Utc));
        if (record) scope!.Remember(destination, copied, Convert.ToHexString(hash!.GetHashAndReset()));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; Current.Value = _previous; _files.Clear();
    }
}
