using System.Buffers.Binary;

namespace AnvilLOD.Plugins;

/// <summary>R16_UNORM DDS and bounds understood by Community/Bottled Shaders TerrainShadows.</summary>
public static class TerrainHeightmap
{
    public sealed record Result(string Path,byte[] Bytes,int Width,int Height,float Minimum,float Maximum);
    public static Result Export(string world,int minX,int minY,int maxX,int maxY,int maxSize,Func<float,float,float?> sample,CancellationToken ct=default)
    {
        int cellsX=checked(maxX-minX+1),cellsY=checked(maxY-minY+1);
        if(cellsX<=0 || cellsY<=0 || maxSize is <1 or >16384)throw new ArgumentException("Invalid heightmap bounds/size.");
        float reduction=Math.Max(1,Math.Max(cellsX*32f,cellsY*32f)/maxSize);
        int width=Math.Max(1,(int)MathF.Ceiling(cellsX*32/reduction)),height=Math.Max(1,(int)MathF.Ceiling(cellsY*32/reduction));
        byte[] bytes=new byte[checked(148+width*height*2)];
        void U(int offset,uint value)=>BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset,4),value);
        U(0,0x20534444);U(4,124);U(8,0x100f);U(12,(uint)height);U(16,(uint)width);U(20,(uint)width*2);
        U(76,32);U(80,4);U(84,0x30315844);U(108,0x1000);U(128,56);U(132,3);U(140,1); // DXGI R16_UNORM, texture2D, one slice
        float lo=float.MaxValue,hi=float.MinValue;
        for(int y=0;y<height;y++)
        {
            ct.ThrowIfCancellationRequested();
            for(int x=0;x<width;x++)
            {
                float wx=(minX+(x+.5f)*cellsX/width)*4096,wy=(maxY+1-(y+.5f)*cellsY/height)*4096;
                float h=sample(wx,wy)??throw new InvalidDataException($"Incomplete heightmap coverage for {world} at ({wx},{wy}). Disable heightmap export for nonrectangular coverage, or supply existing terrain for the gaps.");
                if(!float.IsFinite(h) || h is < -262136 or >262136)throw new InvalidDataException("Heightmap exceeds the supported xLODGen height range.");
                lo=Math.Min(lo,h);hi=Math.Max(hi,h);
                ushort value=(ushort)Math.Clamp(MathF.Round((h+262136)/524272*65535),0,65535);
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(148+(y*width+x)*2,2),value);
            }
        }
        int bottom=(int)MathF.Floor(lo/8),top=Math.Max(bottom+1,(int)MathF.Ceiling(hi/8));
        string path=$"textures\\terrain\\{world}\\{world}.Terrain.HeightMap.{minX}.{minY}.{maxX}.{maxY}.{bottom}.{top}.dds";
        return new(path,bytes,width,height,lo,hi);
    }
}
