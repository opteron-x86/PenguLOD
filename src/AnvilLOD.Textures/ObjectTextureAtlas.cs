using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.Textures;
using AnvilLOD.Textures.Bc;
using AnvilLOD.Textures.Dds;

namespace AnvilLOD.Textures;

/// <summary>Deterministic paired diffuse/normal atlas pages with isolated cells and bounded mip chains.</summary>
public static class ObjectTextureAtlas
{
    public sealed record Input(string Key, TextureImage Diffuse, TextureImage? Normal, bool Alpha, uint ClampMode);
    public sealed record Placement(int Page, AtlasRect Rect);
    public sealed record Page(byte[] Diffuse, byte[] Normal, int Size, int Mips);
    public sealed record Result(IReadOnlyList<Page> Pages, IReadOnlyDictionary<string,Placement> Placements);

    public static Result Build(IReadOnlyList<Input> inputs, int cellSize = 256, int pageSize = 2048, CancellationToken ct = default)
    {
        if (cellSize is <64 or >512 || (cellSize & (cellSize-1)) != 0 || pageSize < cellSize || pageSize > 4096 || (pageSize & (pageSize-1)) != 0)
            throw new ArgumentException("Atlas dimensions must be powers of two; cells 64–512, pages up to 4096.");
        if (inputs.Select(i=>i.Key).Distinct(StringComparer.Ordinal).Count()!=inputs.Count) throw new ArgumentException("Duplicate atlas input keys.");
        var placements = new Dictionary<string,Placement>(StringComparer.Ordinal);
        var pages = new List<Page>();
        int across = pageSize / cellSize, perPage = across * across, gutter = cellSize / 16, inner = cellSize - 2*gutter;
        var sorted = inputs.OrderBy(i=>i.Key,StringComparer.Ordinal).ToArray();
        for (int start=0;start<sorted.Length;start+=perPage)
        {
            ct.ThrowIfCancellationRequested();
            var subset=sorted.Skip(start).Take(perPage).ToArray();
            int grid=1;while(grid*grid<subset.Length)grid*=2;
            int size=grid*cellSize;
            var color=TextureImages.Blank(size,size);var normal=TextureImages.Blank(size,size,normal:true);
            for(int i=0;i<subset.Length;i++)
            {
                ct.ThrowIfCancellationRequested();var source=subset[i];int ox=i%grid*cellSize,oy=i/grid*cellSize;
                Fill(color,source.Diffuse,ox,oy,false,source.Alpha,source.ClampMode);
                Fill(normal,source.Normal??TextureImages.Blank(4,4,normal:true),ox,oy,true,false,source.ClampMode);
                // Half-texel inset and a gutter preserve bilinear sampling at the source boundaries.
                placements[source.Key]=new(pages.Count,new((ox+gutter+.5f)/size,(oy+gutter+.5f)/size,(ox+cellSize-gutter-.5f)/size,(oy+cellSize-gutter-.5f)/size));
            }
            var diffuseMips=new List<byte[]>();var normalMips=new List<byte[]>();
            // Stop while each cell still has a half-texel gutter; a global 1x1 mip mixes unrelated materials.
            var targets=subset.Select((input,i)=>input.Alpha?PbrLodConverter.Coverage(Cell(color,i,grid,cellSize),1):-1).ToArray();
            int cell=cellSize;
            while(true)
            {
                ct.ThrowIfCancellationRequested();
                var shown=(byte[])color.Pixels.Clone();
                if(cell!=cellSize) for(int i=0;i<subset.Length;i++)
                {
                    if(targets[i]<=0 || targets[i]>=.999f)continue;
                    var pixels=Cell(color,i,grid,cell);PbrLodConverter.KeepCoverage(pixels,targets[i]);
                    for(int y=0;y<cell;y++)pixels.AsSpan(y*cell*4,cell*4).CopyTo(shown.AsSpan(((i/grid*cell+y)*color.Width+i%grid*cell)*4,cell*4));
                }
                diffuseMips.Add(Bc7Encoder.EncodeImage(shown,color.Width,color.Height));
                normalMips.Add(Bc7Encoder.EncodeImage(normal.Pixels,normal.Width,normal.Height));
                if(cell<=8)break;
                color=Down(color,false);normal=Down(normal,true);cell/=2;
            }
            byte[] Dds(List<byte[]> mips){using var stream=new MemoryStream();DdsFile.WriteBc7(stream,size,size,mips);return stream.ToArray();}
            pages.Add(new(Dds(diffuseMips),Dds(normalMips),size,diffuseMips.Count));
        }
        return new(pages,placements);

        void Fill(TextureImage output,TextureImage original,int ox,int oy,bool normal,bool alpha,uint clamp)
        {
            if(normal) { original=new((byte[])original.Pixels.Clone(),original.Width,original.Height); for(int i=3;i<original.Pixels.Length;i+=4)original.Pixels[i]=255; }
            var image=TextureImages.Blank(inner,inner,normal);
            // Texture coordinates are normalized: resampling a rectangular source into a square preserves its UV layout.
            TextureImages.Blit(image,original,new TextureTile("","",inner,inner,0,0,1,1,0,1,1,1,alpha),1,normal);
            if(!alpha) for(int a=3;a<image.Pixels.Length;a+=4)image.Pixels[a]=255;
            for(int y=0;y<cellSize;y++)for(int x=0;x<cellSize;x++)
            {
                int px=x-gutter,py=y-gutter;
                px=(clamp&2)!=0?(px%inner+inner)%inner:Math.Clamp(px,0,inner-1);
                py=(clamp&1)!=0?(py%inner+inner)%inner:Math.Clamp(py,0,inner-1);
                image.Pixels.AsSpan((py*inner+px)*4,4).CopyTo(output.Pixels.AsSpan(((oy+y)*output.Width+ox+x)*4,4));
            }
        }
    }

    private static byte[] Cell(TextureImage image,int index,int grid,int cell)
    {
        var pixels=new byte[cell*cell*4];
        for(int y=0;y<cell;y++)image.Pixels.AsSpan(((index/grid*cell+y)*image.Width+index%grid*cell)*4,cell*4).CopyTo(pixels.AsSpan(y*cell*4));
        return pixels;
    }

    private static TextureImage Down(TextureImage image,bool normal)
    {
        var next=TreeAtlasBuilder.Downsample(image.Pixels,image.Width,image.Height);
        if(normal)for(int i=0;i<next.Rgba.Length;i+=4)TextureImages.Normalize(next.Rgba,i);
        return new(next.Rgba,next.W,next.H);
    }
}
