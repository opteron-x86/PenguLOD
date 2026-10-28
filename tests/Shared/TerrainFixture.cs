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
    public TerrainFixture(bool missingCell=false, bool seasons=false, bool treeAlternates=false)
    {
        var mod = new SkyrimMod(ModKey.FromNameAndExtension("Skyrim.esm"), SkyrimRelease.SkyrimSE);
        var water = mod.Waters.AddNew(); water.EditorID="FixtureWater";
        var world = mod.Worldspaces.AddNew(); world.EditorID="FixtureWorld"; world.LodWater.SetTo(water);world.Water.SetTo(water);
        world.LandDefaults=new WorldspaceLandDefaults { DefaultLandHeight=-2048,DefaultWaterHeight=64 };
        var tx = mod.TextureSets.AddNew(); tx.Diffuse=@"red.dds";tx.NormalOrGloss=@"red_n.dds";
        var ltex=mod.LandscapeTextures.AddNew();ltex.EditorID="FixtureLand";ltex.TextureSet.SetTo(tx);
        Tree? tree = null;
        if (seasons)
        {
            var winterTx=mod.TextureSets.AddNew();winterTx.Diffuse=@"green.dds";winterTx.NormalOrGloss=@"green_n.dds";
            var winterLand=mod.LandscapeTextures.AddNew();winterLand.EditorID="WinterLand";winterLand.TextureSet.SetTo(winterTx);
            tree=mod.Trees.AddNew();tree.EditorID="FixtureTree";tree.Model=new Model {File=@"trees\testtree.nif"};
            var winterTree=mod.Trees.AddNew();winterTree.EditorID="WinterTree";winterTree.Model=new Model {File=@"trees\wintertree.nif"};
            if (treeAlternates)
            {
                var normalAlt = new AlternateTexture { Name = "tree:0", Index = 0 }; normalAlt.NewTexture.SetTo(tx);
                var winterAlt = new AlternateTexture { Name = "winter:0", Index = 0 }; winterAlt.NewTexture.SetTo(winterTx);
                tree.Model.AlternateTextures = [normalAlt]; winterTree.Model.AlternateTextures = [winterAlt];
            }
            TextureFixture.Put(Textures.Data,@"meshes\trees\wintertree.nif",AnvilLOD.Meshes.Authoring.LodNifWriter.Write("winter",[TextureFixture.Card("crown",@"textures\red.dds")]));
            TextureFixture.Put(Textures.Data,@"seasons\Fixture_WIN.ini",System.Text.Encoding.UTF8.GetBytes("[LandTextures]\nFixtureLand|WinterLand\n[Trees]\nFixtureTree|WinterTree\n"));
        }
        var block=new WorldspaceBlock { BlockNumberX=-1,BlockNumberY=-1,GroupType=(GroupTypeEnum)4 };
        var sub=new WorldspaceSubBlock { BlockNumberX=-1,BlockNumberY=-1,GroupType=(GroupTypeEnum)5 };block.Items.Add(sub);world.SubCells.Add(block);
        for(int y=-4;y<0;y++)for(int x=-4;x<0;x++)
        {
            if(missingCell && x==-1 && y==-1)continue;
            var cell=new Cell(mod.GetNextFormKey(),SkyrimRelease.SkyrimSE) { Grid=new CellGrid { Point=new P2Int(x,y) },Flags=Cell.Flag.HasWater,WaterHeight=64 };
            var land=new Landscape(mod.GetNextFormKey(),SkyrimRelease.SkyrimSE) { VertexHeightMap=new LandscapeVertexHeightMap { Offset=0,HeightMap=new Array2d<sbyte>(new P2Int(33,33),0) } };
            for(int q=0;q<4;q++) { var layer=new BaseLayer { Header=new LayerHeader { Quadrant=(Quadrant)q } };layer.Header.Texture.SetTo(ltex);land.Layers.Add(layer); }
            if (tree is not null && x==-3 && y==-3)
            {
                var placed=new PlacedObject(mod.GetNextFormKey(),SkyrimRelease.SkyrimSE) {Placement=new Placement {Position=new P3Float(-10000,-10000,0)}};
                placed.Base.SetTo(tree);cell.Temporary.Add(placed);
            }
            cell.Landscape=land;sub.Items.Add(cell);
        }
        mod.WriteToBinary(Path.Combine(Textures.Data,"Skyrim.esm"));
        TextureFixture.Put(Textures.Data,@"lodsettings\FixtureWorld.lod",new LodSettings(-4,-4,4,4,4).ToBytes());
    }
    public void Dispose()=>Textures.Dispose();
}
