using System.Collections.Concurrent;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.World;
using AnvilLOD.Meshes;
using AnvilLOD.Textures;

namespace AnvilLOD.Plugins;

/// <summary>Packs ordinary object surfaces. Unsupported materials keep their original textures and UVs.</summary>
public static class ObjectAtlasStage
{
    public sealed record Report(int PackedMaterials,int Pages,int EligibleParts,int SkippedParts,IReadOnlyDictionary<string,int> Reasons);
    public sealed record Mapping(string Diffuse,string Normal,AtlasRect Rect);
    public sealed record Result(Report Report,Func<LodMesh,LodMesh> Transform);

    public static string? Ineligible(LodMeshPart part)
    {
        var m=part.Material;
        if(m.Passthru is not null)return "passthrough shader";
        if(m.Textures.Count==0 || string.IsNullOrWhiteSpace(m.Textures[0]))return "no diffuse texture";
        if(m.Textures.Skip(2).Any(t=>!string.IsNullOrWhiteSpace(t)) || m.EmissiveColor!=Vector3.Zero || m.EmissiveMultiple>1)return "additional shader channels";
        if(part.UVs.Any(v=>!float.IsFinite(v.X)||!float.IsFinite(v.Y)||v.X<0||v.X>1||v.Y<0||v.Y>1))return "tiling or invalid UVs";
        // Different alpha thresholds need per-material coverage preservation; keep them separate until that is supported.
        if(m.HasAlpha && m.AlphaThreshold!=128)return "nonstandard alpha test";
        return null;
    }

    public static Result Build(IEnumerable<LodMesh> meshes,AssetIndex assets,string output,int cellSize=256,IProgress<string>? progress=null,CancellationToken ct=default)
    {
        var materials=new SortedDictionary<string,LodMaterial>(StringComparer.Ordinal);var reasons=new Dictionary<string,int>();int eligible=0,skipped=0;
        foreach(var mesh in meshes)foreach(var part in mesh.Parts)
        {
            ct.ThrowIfCancellationRequested();string? reason=Ineligible(part);
            if(reason is null && !Exists(part.Material.Textures[0]))reason="diffuse not available";
            if(reason is null && part.Material.Textures.Count>1 && part.Material.Textures[1].Length>0 && !Exists(part.Material.Textures[1]))reason="normal not available";
            if(reason is not null){skipped++;reasons[reason]=reasons.GetValueOrDefault(reason)+1;continue;}
            eligible++;materials[part.Material.Key]=part.Material;
        }
        var mapped=new Dictionary<string,Mapping>(StringComparer.Ordinal);var written=new HashSet<string>();int pages=0;
        // A bounded page batch avoids holding every decoded source image in memory on large profiles.
        int perPage=(2048/cellSize)*(2048/cellSize);
        foreach(var batch in materials.Chunk(perPage))
        {
            ct.ThrowIfCancellationRequested();progress?.Report($"Object atlas: {mapped.Count:N0}/{materials.Count:N0} materials...");
            var inputs=new List<ObjectTextureAtlas.Input>();using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(Encoding.UTF8.GetBytes("atlas1:"+cellSize));
            foreach(var (key,m) in batch)
            {
                hash.AppendData(Encoding.UTF8.GetBytes(key));
                var diffuse=Load(m.Textures[0]);var normal=m.Textures.Count>1 && m.Textures[1].Length>0?Load(m.Textures[1]):null;
                inputs.Add(new(key,diffuse,normal,m.HasAlpha,m.ClampMode));
            }
            var result=ObjectTextureAtlas.Build(inputs,cellSize,2048,ct);
            string stem=@"textures\pengulod\objects\"+Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()[..24];
            for(int page=0;page<result.Pages.Count;page++)
            {
                string diffuse=stem+$"_{page}.dds",normal=stem+$"_{page}_n.dds";
                Write(diffuse,result.Pages[page].Diffuse);Write(normal,result.Pages[page].Normal);pages++;
                foreach(var (key,placement) in result.Placements.Where(p=>p.Value.Page==page))mapped[key]=new(diffuse,normal,placement.Rect);
            }
            TextureImage Load(string path)
            {
                byte[] bytes;string local=GamePath.ToFileSystemPath(output,path);
                if(File.Exists(local))bytes=File.ReadAllBytes(local);
                else {if(!assets.TryOpen(path,out var stream))throw new FileNotFoundException(path);using(stream){using var copy=new MemoryStream();stream.CopyTo(copy);bytes=copy.ToArray();}}
                hash.AppendData(SHA256.HashData(bytes));
                var image=TextureImages.Decode(bytes);
                bool normal=path.EndsWith("_n.dds",StringComparison.OrdinalIgnoreCase);
                if(normal)for(int i=3;i<image.Pixels.Length;i+=4)image.Pixels[i]=255;
                while(image.Width>cellSize || image.Height>cellSize)
                {var down=TreeAtlasBuilder.Downsample(image.Pixels,image.Width,image.Height);image=new(down.Rgba,down.W,down.H);}
                return image;
            }
        }
        var report=new Report(mapped.Count,pages,eligible,skipped,reasons);
        string atlasRoot=GamePath.ToFileSystemPath(output,@"textures\pengulod\objects");
        if(Directory.Exists(atlasRoot))foreach(string file in Directory.EnumerateFiles(atlasRoot,"*.dds"))
            if(!written.Contains(Path.GetFullPath(file)))File.Delete(file);
        File.WriteAllText(Path.Combine(output,"PenguLOD.atlas.json"),JsonSerializer.Serialize(report,PersistentPipeline.Json));
        progress?.Report($"Object atlas: {mapped.Count:N0} materials on {pages} paired pages; {skipped:N0} parts retain original UVs/materials.");
        var cache=new ConcurrentDictionary<LodMesh,LodMesh>(ReferenceEqualityComparer.Instance);
        return new(report,mesh=>cache.GetOrAdd(mesh,m=>Remap(m,mapped)));

        bool Exists(string path)=>File.Exists(GamePath.ToFileSystemPath(output,path))||assets.Exists(path);
        void Write(string relative,byte[] bytes){string path=GamePath.ToFileSystemPath(output,relative);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,bytes);written.Add(Path.GetFullPath(path));}
    }

    public static LodMesh Remap(LodMesh mesh,IReadOnlyDictionary<string,Mapping> mappings) => new()
    {
        Path=mesh.Path,Warnings=mesh.Warnings,NoShapesInFile=mesh.NoShapesInFile,
        Parts=mesh.Parts.Select(p=>
        {
            if(Ineligible(p) is not null || !mappings.TryGetValue(p.Material.Key,out var map))return p;
            var textures=p.Material.Textures.Concat(Enumerable.Repeat("",Math.Max(0,2-p.Material.Textures.Count))).ToArray();textures[0]=map.Diffuse;textures[1]=map.Normal;
            return new LodMeshPart {Name=p.Name,TexGenScale=p.TexGenScale,Material=p.Material with {Textures=textures,ClampMode=0},
                UVs=p.UVs.Select(v=>new Vector2(map.Rect.U0+v.X*(map.Rect.U1-map.Rect.U0),map.Rect.V0+v.Y*(map.Rect.V1-map.Rect.V0))).ToArray(),
                Positions=p.Positions,Normals=p.Normals,Tangents=p.Tangents,Bitangents=p.Bitangents,Colors=p.Colors,Triangles=p.Triangles,Tier=p.Tier};
        }).ToArray()
    };
}
