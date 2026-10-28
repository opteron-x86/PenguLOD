using AnvilLOD.Core.World;
using AnvilLOD.Plugins;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AnvilLOD.Testing;

public sealed class TerrainFixture : IDisposable
{
    public TextureFixture Textures { get; } = new();
    public string Root => Textures.Root;
    public string Output => Path.Combine(Root, "Terrain output");
    public GameContextOptions Game => Textures.Game;
    public TerrainGenerationRequest Request => new(Game, Output, ["FixtureWorld"], TextureSize:64);
    public TerrainFixture(bool missingCell=false)
    {
        var mod = new SkyrimMod(ModKey.FromNameAndExtension("Skyrim.esm"), SkyrimRelease.SkyrimSE);
        var water = mod.Waters.AddNew(); water.EditorID="FixtureWater";
        var world = mod.Worldspaces.AddNew(); world.EditorID="FixtureWorld"; world.LodWater.SetTo(water);world.Water.SetTo(water);
        world.LandDefaults=new WorldspaceLandDefaults { DefaultLandHeight=-2048,DefaultWaterHeight=64 };
        var tx = mod.TextureSets.AddNew(); tx.Diffuse=@"red.dds";tx.NormalOrGloss=@"red_n.dds";
        var ltex=mod.LandscapeTextures.AddNew();ltex.TextureSet.SetTo(tx);
        var block=new WorldspaceBlock { BlockNumberX=-1,BlockNumberY=-1,GroupType=(GroupTypeEnum)4 };
        var sub=new WorldspaceSubBlock { BlockNumberX=-1,BlockNumberY=-1,GroupType=(GroupTypeEnum)5 };block.Items.Add(sub);world.SubCells.Add(block);
        for(int y=-4;y<0;y++)for(int x=-4;x<0;x++)
        {
            if(missingCell && x==-1 && y==-1)continue;
            var cell=new Cell(mod.GetNextFormKey(),SkyrimRelease.SkyrimSE) { Grid=new CellGrid { Point=new P2Int(x,y) },Flags=Cell.Flag.HasWater,WaterHeight=64 };
            var land=new Landscape(mod.GetNextFormKey(),SkyrimRelease.SkyrimSE) { VertexHeightMap=new LandscapeVertexHeightMap { Offset=0,HeightMap=new Array2d<sbyte>(new P2Int(33,33),0) } };
            for(int q=0;q<4;q++) { var layer=new BaseLayer { Header=new LayerHeader { Quadrant=(Quadrant)q } };layer.Header.Texture.SetTo(ltex);land.Layers.Add(layer); }
            cell.Landscape=land;sub.Items.Add(cell);
        }
        mod.WriteToBinary(Path.Combine(Textures.Data,"Skyrim.esm"));
        TextureFixture.Put(Textures.Data,@"lodsettings\FixtureWorld.lod",new LodSettings(-4,-4,4,4,4).ToBytes());
    }
    public void Dispose()=>Textures.Dispose();
}
