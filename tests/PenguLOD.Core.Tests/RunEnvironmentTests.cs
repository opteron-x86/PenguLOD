using PenguLOD.Plugins;

namespace PenguLOD.Core.Tests;

/// <summary>The game running, a mod manager's mount on the Data folder, and reads that fail when that mount changes.</summary>
public sealed class RunEnvironmentTests
{
    [Fact]
    public void TheGameIsFoundByItsExecutableUnderWineOrNatively()
    {
        Assert.Equal("SkyrimSE.exe (process 42)", RunEnvironment.RunningGame([
            (7, "/usr/bin/kate\0-b\0/home/user/SkyrimSE.exe.log\0"),
            (9, null), (10, ""),
            (42, "Z:\\home\\user\\Skyrim Special Edition\\\\SkyrimSE.exe\0"),
        ]));
        Assert.Equal("SkyrimVR.exe (process 5)", RunEnvironment.RunningGame([(5, "/games/SkyrimVR/skyrimvr.EXE\0-forcesteamloader\0")]));
        // The script extender's loader exits once the game has started, and a path that merely contains the name is not the game.
        Assert.Null(RunEnvironment.RunningGame([(3, "c:\\windows\\system32\\steam.exe\0/games/skse64_loader.exe\0"), (4, "/opt/NotSkyrimSE.exe\0")]));
    }

    [Fact]
    public void OnlyAFuseMountOnTheDataFolderItselfIsReported()
    {
        const string Data = "/home/user/Amaranth/Skyrim Special Edition/Data";
        string[] mounts =
        [
            "60 1 0:30 /@home /home rw,noatime shared:30 - btrfs /dev/nvme0n1p2 rw,ssd,subvol=/@home",
            "413 39 0:88 / /run/user/1000/doc rw,nosuid,nodev,relatime shared:671 - fuse.portal portal rw,user_id=1000,group_id=1000",
            @"345 60 0:104 / /home/user/Amaranth/Skyrim\040Special\040Edition/Data rw,nosuid,nodev,noatime shared:267 - fuse mo2linux rw,user_id=1000,group_id=1000,max_read=1048576",
        ];
        Assert.Equal("fuse (mo2linux)", RunEnvironment.DataMount(Data, mounts));
        Assert.Equal("fuse (mo2linux)", RunEnvironment.DataMount(Data + "/", mounts));
        Assert.Null(RunEnvironment.DataMount(Data, mounts[..2]));
        Assert.Null(RunEnvironment.DataMount("/home/user/Amaranth/Skyrim Special Edition", mounts));
        // A game on a FUSE drive (NTFS through ntfs-3g) is ordinary storage: the mount point is above the Data folder.
        Assert.Null(RunEnvironment.DataMount("/mnt/games/Skyrim/Data", ["90 1 8:17 / /mnt/games rw shared:40 - fuseblk /dev/sdb1 rw,user_id=0"]));
        // The last mount on a path is the one in view.
        Assert.Null(RunEnvironment.DataMount(Data, [.. mounts, @"400 345 0:31 / /home/user/Amaranth/Skyrim\040Special\040Edition/Data rw - ext4 /dev/sdc1 rw"]));
        Assert.Null(RunEnvironment.DataMount(Data, ["not a mountinfo line", ""]));
    }

    [Fact]
    public void AReadWhoseStorageWentAwayIsExplainedAndOtherFailuresAreNot()
    {
        Assert.Null(RunEnvironment.DescribeLostStorage(new FileNotFoundException("Missing landscape texture: a.dds", "a.dds")));
        Assert.Null(RunEnvironment.DescribeLostStorage(new IOException("No space left on device", 28)));
        Assert.Null(RunEnvironment.DescribeLostStorage(new InvalidDataException("Texture output is incomplete.")));
        if (!OperatingSystem.IsLinux()) return;
        // Address zero of a process is never mapped, so the kernel answers this read with EIO, as a replaced FUSE mount does.
        IOException real;
        using (var memory = File.OpenHandle("/proc/self/mem"))
            real = Assert.ThrowsAny<IOException>(() => RandomAccess.Read(memory, new byte[16], 0));
        Assert.True(RunEnvironment.LostStorage(real), $"HResult {real.HResult}: {real.Message}");
        // Plugin readers wrap the failure, sometimes more than once.
        string? text = RunEnvironment.DescribeLostStorage(new InvalidOperationException("ccBGSSSE025-AdvDSGS.esm", new AggregateException(new ArgumentException("other"), real)));
        Assert.NotNull(text);
        Assert.Contains("could not be read partway through the run", text); Assert.Contains(real.Message.TrimEnd('.'), text);
        Assert.Contains("run the same selection again", text);
        Assert.DoesNotContain("   at ", text);
    }
}
