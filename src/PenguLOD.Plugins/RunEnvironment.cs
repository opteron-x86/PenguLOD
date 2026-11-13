using System.Diagnostics;

namespace PenguLOD.Plugins;

/// <summary>
/// Things outside PenguLOD that change the game's files during a run. A mod manager on Linux mounts a virtual file system
/// over the game's Data folder while the game runs; plugins opened through it, or before it appeared, fail to read once
/// it is mounted, replaced or removed.
/// </summary>
public static class RunEnvironment
{
    public const string GameRunningCode = "game-running", DataMountedCode = "data-folder-mounted";
    private static readonly string[] GameExecutables = ["SkyrimSE.exe", "SkyrimVR.exe"];

    /// <summary>The running game as "SkyrimSE.exe (process 1234)", or null.</summary>
    public static string? RunningGame()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                foreach (string exe in GameExecutables)
                {
                    var found = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe));
                    try { if (found.Length > 0) return $"{exe} (process {found[0].Id})"; }
                    finally { foreach (var process in found) process.Dispose(); }
                }
                return null;
            }
            if (!OperatingSystem.IsLinux() || !Directory.Exists("/proc")) return null;
            return RunningGame(Directory.EnumerateDirectories("/proc").Select(Path.GetFileName)
                .Where(name => int.TryParse(name, out _)).Select(name => (int.Parse(name!), CommandLine(name!))));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception) { return null; }
        static string? CommandLine(string pid)
        {
            // Processes exit while the list is read, and other users' processes cannot be read.
            try { return File.ReadAllText($"/proc/{pid}/cmdline"); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
        }
    }

    /// <summary>Finds the game among NUL-separated command lines. Under Wine the first argument is a Windows path.</summary>
    internal static string? RunningGame(IEnumerable<(int Pid, string? CommandLine)> processes)
    {
        foreach (var (pid, line) in processes)
        {
            if (string.IsNullOrEmpty(line)) continue;
            string program = line.Split('\0')[0];
            string name = program[(program.LastIndexOfAny(['/', '\\']) + 1)..];
            if (GameExecutables.FirstOrDefault(exe => exe.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } match) return $"{match} (process {pid})";
        }
        return null;
    }

    /// <summary>The file system mounted on the Data folder itself when it is a FUSE one, as "fuse (mo2linux)"; otherwise null.
    /// A game installed on a FUSE drive is not reported: its mount point is above the Data folder.</summary>
    public static string? DataMount(string dataFolder)
    {
        if (!OperatingSystem.IsLinux()) return null;
        try { return DataMount(Resolved(dataFolder), File.ReadLines("/proc/self/mountinfo")); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <param name="mountinfo">Lines of /proc/self/mountinfo: "id parent major:minor root mountpoint options... - type source options".</param>
    internal static string? DataMount(string dataFolder, IEnumerable<string> mountinfo)
    {
        string wanted = dataFolder.TrimEnd('/');
        string? found = null;
        foreach (string line in mountinfo)
        {
            int separator = line.IndexOf(" - ", StringComparison.Ordinal);
            if (separator < 0) continue;
            string[] before = line[..separator].Split(' '), after = line[(separator + 3)..].Split(' ');
            if (before.Length < 5 || after.Length < 2) continue;
            if (!Unescape(before[4]).TrimEnd('/').Equals(wanted, StringComparison.Ordinal)) continue;
            // Later lines are mounted over earlier ones, so the last match is the one in view.
            found = after[0].StartsWith("fuse", StringComparison.Ordinal) ? $"{after[0]} ({Unescape(after[1])})" : null;
        }
        return found;
        // The kernel writes space, tab, newline and backslash as three-digit octal escapes.
        static string Unescape(string value)
        {
            if (!value.Contains('\\')) return value;
            var text = new System.Text.StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == '\\' && i + 3 < value.Length && value.AsSpan(i + 1, 3) is var digits && digits[0] is >= '0' and <= '3' && digits[1] is >= '0' and <= '7' && digits[2] is >= '0' and <= '7')
                { text.Append((char)((digits[0] - '0') * 64 + (digits[1] - '0') * 8 + digits[2] - '0')); i += 3; }
                else text.Append(value[i]);
            }
            return text.ToString();
        }
    }

    /// <summary>The path with symbolic links in every component followed, as the kernel lists mount points.</summary>
    private static string Resolved(string path)
    {
        string current = "/";
        foreach (string part in Path.GetFullPath(path).Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            try { if (new DirectoryInfo(current).ResolveLinkTarget(returnFinalTarget: true) is { } target) current = target.FullName; }
            catch (IOException) { }
        }
        return current;
    }

    /// <summary>
    /// A plain account of a read that failed because the storage under an open file went away or changed, or null for any
    /// other failure. Missing files, denied access and a full disk have their own messages.
    /// </summary>
    public static string? DescribeLostStorage(Exception error)
    {
        if (Find(error) is not { } io) return null;
        return $"A file could not be read partway through the run: {io.Message.TrimEnd('.')}. The storage under it changed or went away. "
            + "This happens when the game is started or closed from a mod manager that mounts a virtual Data folder, and when a drive disconnects. "
            + "Close the game, then run the same selection again; do not start or close the game until it finishes. "
            + "If neither applies, check the drive that holds the file.";
        static IOException? Find(Exception? e)
        {
            for (int depth = 0; e is not null && depth < 16; depth++)
            {
                if (e is AggregateException many) return many.InnerExceptions.Select(Find).FirstOrDefault(found => found is not null);
                if (e is IOException io && LostStorage(io)) return io;
                e = e.InnerException;
            }
            return null;
        }
    }

    /// <summary>True for the operating system's "the device or connection behind this file is gone" errors.</summary>
    internal static bool LostStorage(IOException error)
    {
        if (error is FileNotFoundException or DirectoryNotFoundException or PathTooLongException or EndOfStreamException or DriveNotFoundException) return false;
        // On Unix an unmapped errno is the HResult itself; on Windows the Win32 code is the low word of 0x8007xxxx.
        if (OperatingSystem.IsWindows())
            return (error.HResult & 0xFFFF0000) == unchecked((int)0x80070000) && (error.HResult & 0xFFFF) is
                21      // ERROR_NOT_READY
                or 55   // ERROR_DEV_NOT_EXIST
                or 59   // ERROR_UNEXP_NET_ERR
                or 64   // ERROR_NETNAME_DELETED
                or 1006 // ERROR_FILE_INVALID: the volume was externally altered
                or 1117 // ERROR_IO_DEVICE
                or 1167; // ERROR_DEVICE_NOT_CONNECTED
        return error.HResult is
            5       // EIO: what a replaced FUSE mount returns for a file opened through it
            or 6    // ENXIO
            or 19   // ENODEV
            or 103  // ECONNABORTED
            or 107  // ENOTCONN: the FUSE daemon exited
            or 116; // ESTALE
    }
}
