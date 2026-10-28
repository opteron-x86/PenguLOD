using System.Diagnostics;
using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using AnvilLOD.Core.Paths;

namespace AnvilLOD.Core.Pipeline;

/// <summary>Content hashes of published files. Paths are relative and never followed through links.</summary>
public sealed class OutputSnapshot
{
    public Dictionary<string, string> Files { get; set; } = new(StringComparer.Ordinal);
    public string Digest => HashText(string.Join('\n', Files.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key + "=" + x.Value)));
    public static string HashText(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    public static string HashFile(string file, CancellationToken ct = default)
    {
        var before = new FileInfo(file);
        long length = before.Length, modified = before.LastWriteTimeUtc.Ticks;
        using var input = File.OpenRead(file);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
        try
        {
            int count;
            while ((count = input.Read(buffer)) != 0) { ct.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, count); }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
        before.Refresh();
        if (before.Length != length || before.LastWriteTimeUtc.Ticks != modified)
            throw new IOException("File changed while being verified: " + file);
        ct.ThrowIfCancellationRequested();
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public static OutputSnapshot Capture(string root, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var result = new OutputSnapshot();
        var watch = Stopwatch.StartNew();
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out var dir))
        {
            ct.ThrowIfCancellationRequested(); RejectLink(root, dir);
            foreach (var path in Directory.EnumerateFileSystemEntries(dir))
            {
                ct.ThrowIfCancellationRequested(); RejectLink(root, path);
                if (Directory.Exists(path)) { pending.Push(path); continue; }
                if (Path.GetFileName(path).Equals("meta.ini", StringComparison.OrdinalIgnoreCase)) continue;
                result.Files.Add(Path.GetRelativePath(root, path).Replace('\\', '/'), HashFile(path, ct));
                if (watch.Elapsed.TotalSeconds >= 1)
                { progress?.Report($"Recording output hashes: {result.Files.Count:N0} files..."); watch.Restart(); }
            }
        }
        if (result.Files.Count == 0) throw new InvalidDataException("The stage produced no output files: " + root);
        return result;
    }

    public void Verify(string root, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (Files.Count == 0) throw new InvalidDataException("The saved stage contains no output hashes.");
        var watch = Stopwatch.StartNew(); int done = 0;
        foreach (var (relative, expected) in Files)
        {
            ct.ThrowIfCancellationRequested();
            string path = Resolve(root, relative);
            if (!HashFile(path, ct).Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Published output was changed: " + path);
            done++;
            if (watch.Elapsed.TotalSeconds >= 1)
            { progress?.Report($"Verifying completed stage: {done:N0}/{Files.Count:N0} files..."); watch.Restart(); }
        }
    }

    public static string Resolve(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains('\\')
            || relative.Split('/').Any(p => p is "" or "." or ".." || p.Contains(':')))
            throw new InvalidDataException("Invalid checkpoint output path: " + relative);
        string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        for (string? part = path; part is not null && HostPath.IsWithin(part, root); part = Path.GetDirectoryName(part))
            RejectLink(root, part);
        return path;
    }

    private static void RejectLink(string root, string path)
    {
        FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        if (info.LinkTarget is not null || !HostPath.IsWithin(path, root))
            throw new IOException("Stage output contains a symbolic link or escaping path: " + path);
    }
}
