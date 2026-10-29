using System.Numerics;
using AnvilLOD.Core.World;
using AnvilLOD.Plugins;
using AnvilLOD.Testing;
using AnvilLOD.Textures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace AnvilLOD.Core.Tests;

public sealed class TerrainMaterialTests
{
    [Theory]
    [InlineData(1, 0, 1, 0)]
    [InlineData(0, 1, 0, -1)]
    [InlineData(-1, 0, -1, 0)]
    [InlineData(0, -1, 0, 1)]
    public void DirectXDetailUsesEastSouthAxesInSkyrimsEastUpNorthNormalMap(float x,float y,float east,float north)
    {
        var result=TerrainMaterials.Normal(Vector3.UnitY,new(x,y,1),1);
        Assert.Equal(east/MathF.Sqrt(2),result.X,5);Assert.Equal(north/MathF.Sqrt(2),result.Z,5);
        Assert.Equal(1/MathF.Sqrt(2),result.Y,5);
        var slope=Vector3.Normalize(new Vector3(-.25f,1,-.5f));
        Assert.True(Vector3.Distance(slope,TerrainMaterials.Normal(slope,Vector3.UnitZ,1))<1e-6);
        Assert.True(Vector3.Distance(slope,TerrainMaterials.Normal(slope,new(x,y,1),0))<1e-6);
    }

    [Fact]
    public void SourceNormalStrengthChangesTerrainDdsAndMissingOptionalMapsAreReported()
    {
        using var f=new TerrainFixture();
        TextureFixture.Put(f.Textures.Data,@"textures\red_n.dds",TextureFixture.Solid(218,128,218));
        TerrainGeneration.Run(f.Request);
        var normal=Image(f,"_n");Assert.InRange(normal.Pixels[0],212,222);Assert.InRange(normal.Pixels[1],212,222);
        TerrainGeneration.Run(f.Request with {Materials=new(NormalStrength:0)});
        normal=Image(f,"_n");Assert.InRange(normal.Pixels[0],125,131);Assert.InRange(normal.Pixels[1],250,255);
        File.Delete(GamePath.ToFileSystemPath(f.Textures.Data,@"textures\red_n.dds"));
        var result=TerrainGeneration.Run(f.Request);
        Assert.Single(result.Warnings,w=>w.Contains("optional terrain normal"));
        Assert.InRange(Image(f,"_n").Pixels[1],250,255);
        string before=AnvilLOD.Core.Pipeline.OutputSnapshot.HashFile(GamePath.ToFileSystemPath(f.Output,TerrainGeneration.ManifestName));
        File.Delete(GamePath.ToFileSystemPath(f.Textures.Data,@"textures\red.dds"));
        Assert.Throws<FileNotFoundException>(()=>TerrainGeneration.Run(f.Request));
        Assert.Equal(before,AnvilLOD.Core.Pipeline.OutputSnapshot.HashFile(GamePath.ToFileSystemPath(f.Output,TerrainGeneration.ManifestName)));
    }

    [Fact]
    public void AlphaLayersBlendColourAndNormalsWithTheSameWeights()
    {
        using var f=new TerrainFixture(editMod:mod=>
        {
            var tx=mod.TextureSets.AddNew();tx.Diffuse="green.dds";tx.NormalOrGloss="green_n.dds";
            var land=mod.LandscapeTextures.AddNew();land.TextureSet.SetTo(tx);
            foreach(var cell in mod.Worldspaces.Single().SubCells.SelectMany(b=>b.Items).SelectMany(b=>b.Items))
            for(int q=0;q<4;q++)
            {
                var layer=new AlphaLayer {Header=new LayerHeader {Quadrant=(Quadrant)q,LayerNumber=1},AlphaLayerData=[]};
                layer.Header.Texture.SetTo(land);
                for(ushort i=0;i<289;i++)layer.AlphaLayerData.Add(new AlphaLayerData {Position=i,Opacity=.5f});
                cell.Landscape!.Layers.Add(layer);
            }
        });
        TextureFixture.Put(f.Textures.Data,@"textures\red_n.dds",TextureFixture.Solid(218,128,218));
        TextureFixture.Put(f.Textures.Data,@"textures\green_n.dds",TextureFixture.Solid(38,128,218));
        TerrainGeneration.Run(f.Request);
        Assert.InRange(Image(f,"_n").Pixels[0],124,132);Assert.InRange(Image(f,"_n").Pixels[1],250,255);
        Assert.InRange(Image(f,"").Pixels[0],170,180);Assert.InRange(Image(f,"").Pixels[1],167,177);
    }

    [Theory]
    [InlineData(false,false)] [InlineData(false,true)] [InlineData(true,false)]
    public void PbrSelectionIsExplicitAndCalibrationUsesBlueAo(bool explicitPath,bool twins)
    {
        using var f=new TerrainFixture(editMod:mod=>
        {
            if(explicitPath) {var tx=mod.TextureSets.First();tx.Diffuse=@"pbr\red.dds";tx.NormalOrGloss=@"pbr\red_n.dds";}
        });
        TextureFixture.Put(f.Textures.Data,@"textures\pbr\red.dds",TextureFixture.Solid(8,235,10));
        TextureFixture.Put(f.Textures.Data,@"textures\pbr\red_n.dds",TextureFixture.Solid(128,128,255));
        TextureFixture.Put(f.Textures.Data,@"textures\pbr\red_rmaos.dds",TextureFixture.Solid(240,240,0));
        var result=TerrainGeneration.Run(f.Request with {Materials=new(PbrTwins:twins,PbrScale:1,PbrGamma:1,OcclusionStrength:.5f)});
        bool pbr=explicitPath||twins; Assert.Equal(pbr,Assert.Single(result.Materials!).Pbr);
        if(pbr) Assert.InRange(Image(f,"").Pixels[1],168,176); else Assert.InRange(Image(f,"").Pixels[0],230,245);
        if(pbr)
        {
            TerrainGeneration.Run(f.Request with {Materials=new(PbrTwins:twins,PbrScale:1,PbrGamma:1,OcclusionStrength:0)});
            Assert.InRange(Image(f,"").Pixels[1],230,240);
        }
    }

    [Fact]
    public void MipsAverageLinearColourWithoutTreatingGlossAlphaAsTransparency()
    {
        using var f=new TextureFixture();
        byte[] pixels=new byte[8*8*4];
        for(int i=0;i<64;i++) {byte c=(byte)(i%2*255);pixels[i*4]=pixels[i*4+1]=pixels[i*4+2]=c;pixels[i*4+3]=(byte)(255-c);}
        // Uncompressed DDS keeps the deliberately transparent white pixels intact.
        TextureFixture.Put(f.Data,@"textures\contrast.dds",RawDds(pixels));
        var materials=new TerrainMaterials(AssetIndex.Build(GameRelease.SkyrimSE,f.Data),new(),default);
        var sample=materials.Sample(@"textures\contrast.dds",null,null,.25f,.25f,1);
        Assert.InRange(sample.Color.X,.49f,.51f);Assert.InRange(TerrainSource.Srgb(sample.Color.X),186,189);
    }

    [Fact]
    public void DefaultNormalIniAndSeasonalNormalsReachTheOutput()
    {
        using(var f=new TerrainFixture(editLandscape:l=>l.Layers.Clear()))
        {
            string ini=Path.Combine(f.Root,"terrain.ini");
            File.WriteAllText(ini,"[Landscape]\nsDefaultLandNormalTexture=custom_n.dds\n");
            TextureFixture.Put(f.Textures.Data,@"textures\landscape\dirt02.dds",TextureFixture.Solid(100,100,100));
            TextureFixture.Put(f.Textures.Data,@"textures\landscape\custom_n.dds",TextureFixture.Solid(128,218,218));
            TerrainGeneration.Run(f.Request with {Game=f.Game with {GameIniPath=ini}});
            Assert.InRange(Image(f,"_n").Pixels[2],32,42);
        }
        using(var f=new TerrainFixture(seasons:true))
        {
            TextureFixture.Put(f.Textures.Data,@"textures\green_n.dds",TextureFixture.Solid(128,218,218));
            TerrainGeneration.Run(f.Request with {Seasons=true});
            Assert.InRange(Image(f,"_n").Pixels[2],125,131);
            Assert.InRange(Image(f,".WIN_n").Pixels[2],32,42);
        }
    }

    private static byte[] RawDds(byte[] pixels)
    {
        var bytes=new byte[128+pixels.Length];
        void Set(int at,uint value)=>System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at),value);
        Set(0,0x20534444);Set(4,124);Set(8,0x100F);Set(12,8);Set(16,8);Set(20,32);
        Set(76,32);Set(80,0x41);Set(88,32);Set(92,0xff);Set(96,0xff00);Set(100,0xff0000);Set(104,0xff000000);Set(108,0x1000);
        pixels.CopyTo(bytes,128);return bytes;
    }
    private static TextureImage Image(TerrainFixture f,string suffix) => TextureImages.Decode(File.ReadAllBytes(
        GamePath.ToFileSystemPath(f.Output,$@"textures\terrain\FixtureWorld\FixtureWorld.4.-4.-4{suffix}.dds")));
}
