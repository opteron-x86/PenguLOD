using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using AnvilLOD.Core;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.World;

namespace AnvilLOD.Plugins;

internal sealed record TerrainTileReceipt(TerrainTileResult Tile, OutputSnapshot Output,
    IReadOnlyList<QuadKey> FallbackTiles, IReadOnlyList<TerrainMaterialReport> Materials, IReadOnlyList<string> Warnings);

/// <summary>Private job cache. Complete mesh/diffuse/normal sets are committed together and verified while copying.</summary>
internal sealed class TerrainTileCache
{
    private readonly string _root;
    private readonly bool _reuse;
    private readonly CancellationToken _ct;
    private readonly IProgress<string>? _progress;
    internal TerrainTileCache(string folder,string fingerprint,bool reuse,IProgress<string>? progress,CancellationToken ct)
    {
        _root=Path.Combine(folder,OutputSnapshot.HashText(ProductInfo.Version+"|terrain-tiles-v1|"+fingerprint));
        _reuse=reuse;_ct=ct;_progress=progress;
        // Resolve against the job folder so existing links at any cache level are rejected.
        OutputSnapshot.Resolve(folder,Path.GetFileName(_root)+"/probe");
        Directory.CreateDirectory(_root);
    }
    private string Folder(TerrainTileResult tile) => OutputSnapshot.Resolve(_root,
        OutputSnapshot.HashText($"{tile.Worldspace}|{tile.Level}|{tile.X}|{tile.Y}|{tile.Season}")+"/entry.json");

    internal TerrainTileReceipt? Restore(TerrainTileResult tile,IReadOnlyCollection<string> expectedFiles,string destination)
    {
        if(!_reuse)return null;
        string metadata=Folder(tile);
        if(!File.Exists(metadata))return null;
        try
        {
            _ct.ThrowIfCancellationRequested();
            var receipt=JsonSerializer.Deserialize<TerrainTileReceipt>(File.ReadAllText(metadata),PersistentPipeline.Json);
            if(receipt is null || receipt.Tile is null || receipt.Output?.Files is null || receipt.FallbackTiles is null || receipt.Materials is null || receipt.Warnings is null
                || receipt.Tile.Worldspace!=tile.Worldspace || receipt.Tile.Level!=tile.Level || receipt.Tile.X!=tile.X || receipt.Tile.Y!=tile.Y || receipt.Tile.Season!=tile.Season
                || !receipt.Output.Files.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(expectedFiles))
                throw new InvalidDataException("Tile checkpoint does not match the requested files.");
            foreach(var (relative,hash) in receipt.Output.Files)
                Copy(OutputSnapshot.Resolve(Path.GetDirectoryName(metadata)!,relative),OutputSnapshot.Resolve(destination,relative),hash);
            return receipt;
        }
        catch(Exception e) when(e is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        { _progress?.Report($"Rebuilding terrain tile {tile.Worldspace} LOD{tile.Level} ({tile.X},{tile.Y}) {tile.Season}: checkpoint verification failed ({e.Message}).");return null; }
    }

    internal void Store(TerrainTileReceipt receipt,string source)
    {
        string final=Path.GetDirectoryName(Folder(receipt.Tile))!;
        string pending=final+".pending-"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(pending);
        try
        {
            foreach(var (relative,hash) in receipt.Output.Files)
                Copy(OutputSnapshot.Resolve(source,relative),OutputSnapshot.Resolve(pending,relative),hash);
            _ct.ThrowIfCancellationRequested();
            File.WriteAllText(Path.Combine(pending,"entry.json"),JsonSerializer.Serialize(receipt,PersistentPipeline.Json));
            if(Directory.Exists(final))Directory.Delete(final,true);
            Directory.Move(pending,final);
        }
        finally {if(Directory.Exists(pending))Directory.Delete(pending,true);}
    }

    private void Copy(string source,string destination,string expected)
    {
        _ct.ThrowIfCancellationRequested();Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var input=File.OpenRead(source);using var output=File.Create(destination);
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer=ArrayPool<byte>.Shared.Rent(128*1024);
        try
        {
            int length;while((length=input.Read(buffer))>0)
            { _ct.ThrowIfCancellationRequested();hash.AppendData(buffer,0,length);output.Write(buffer,0,length); }
            if(!Convert.ToHexString(hash.GetHashAndReset()).Equals(expected,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Terrain tile content hash changed: "+source);
        }
        finally {ArrayPool<byte>.Shared.Return(buffer);}
    }
}
