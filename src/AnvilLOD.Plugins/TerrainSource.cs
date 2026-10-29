using System.Numerics;
using AnvilLOD.Core.World;
using AnvilLOD.Meshes.Nif;
using AnvilLOD.Textures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AnvilLOD.Plugins;

/// <summary>Winning LAND plus existing BTR/texture coverage for the landscape stripped from Bethesda plugins.</summary>
internal sealed class TerrainSource
{
    internal sealed record Layer(string Texture, string? Normal, int Quadrant, int Order, float[]? Opacity, string? Occlusion = null);
    internal sealed record Cell(float[] Heights, Vector3[]? Colors, Layer[] Layers)
    {
        internal Layer?[] Bases { get; } = Enumerable.Range(0, 4).Select(q => Layers.FirstOrDefault(l => l.Quadrant == q && l.Opacity is null)).ToArray();
        internal Layer[][] Alpha { get; } = Enumerable.Range(0, 4).Select(q => Layers.Where(l => l.Quadrant == q && l.Opacity is not null).ToArray()).ToArray();
    }
    private readonly string? _season;
    private readonly string _defaultDiffuse;
    private readonly string _defaultNormal;
    private readonly TerrainMaterials _materials;
    private readonly TerrainMaterialSettings _materialSettings;
    internal IReadOnlyCollection<string> MaterialWarnings => _materials.Warnings;
    internal IEnumerable<TerrainMaterialReport> MaterialReports => _materials.Reports;
    private readonly AssetIndex _assets;
    private readonly string _world;
    private readonly LodGrid _grid;
    private readonly bool _fallback;
    private readonly CancellationToken _ct;
    internal readonly Dictionary<CellCoord, Cell> Cells = [];
    internal readonly TerrainHeights Heights = new();
    private readonly Dictionary<CellCoord, float> _water = [];
    private readonly Dictionary<QuadKey, TriangleField?> _meshes = [];
    private readonly Dictionary<QuadKey, TriangleField?> _waterMeshes = [];
    private readonly Dictionary<(int X,int Y),float?> _fallbackVertices = [];
    private static readonly LodLevel[] TerrainLevels = [LodLevel.Lod4,LodLevel.Lod8,LodLevel.Lod16,LodLevel.Lod32];
    private readonly HashSet<CellCoord> _knownCells = [];
    private readonly HashSet<QuadKey> _fallbackUsed = [];
    public IReadOnlyCollection<QuadKey> FallbackUsed => _fallbackUsed;
    internal bool HasLodWater { get; }
    internal void RestoreFallbackTiles(IEnumerable<QuadKey> tiles) => _fallbackUsed.UnionWith(tiles);

    internal TerrainSource(GameContext game, AssetIndex assets, IWorldspaceGetter world, LodGrid grid, bool fallback, CancellationToken ct, IReadOnlyDictionary<FormKey,FormKey>? swaps = null, string? season = null, string defaultDiffuse = TerrainDefaultTexture.SkyrimDefault,
        string defaultNormal = TerrainDefaultTexture.SkyrimNormal, TerrainMaterialSettings? materials = null)
    {
        _season = season; _defaultDiffuse = defaultDiffuse; _assets = assets; _world = grid.Worldspace; _grid = grid; _fallback = fallback; _ct = ct;
        _defaultNormal = defaultNormal; _materialSettings = materials ?? new(); _materialSettings.Validate();
        _materials = new(assets, _materialSettings, ct);
        HasLodWater = world.LodWater.FormKeyNullable is { IsNull: false };
        foreach (var ctx in game.LoadOrder.PriorityOrder.Landscape().WinningContextOverrides(game.LinkCache))
        {
            ct.ThrowIfCancellationRequested();
            if (ctx.Record.IsDeleted || !ctx.TryGetParentSimpleContext<ICellGetter>(out var parent) || parent.Record.Grid is not { } point
                || !ctx.TryGetParentSimpleContext<IWorldspaceGetter>(out var ws) || ws.Record.FormKey != world.FormKey) continue;
            if (ctx.Record.VertexHeightMap is not { } vh) continue;
            var coord = new CellCoord(point.Point.X, point.Point.Y);
            if (!grid.IsInsideGrid(coord)) continue;
            var h = TerrainHeights.Decode(vh.Offset, (x,y) => vh.HeightMap[x,y]);
            if (h.Any(v => !float.IsFinite(v))) throw new InvalidDataException($"Invalid LAND heights in {_world} {coord}.");
            var layers = new List<Layer>();
            foreach (var layer in ctx.Record.Layers)
            {
                var header = layer.Header ?? throw new InvalidDataException("Missing landscape layer header.");
                string diffuse = _defaultDiffuse; string? normal = _defaultNormal, occlusion = null;
                if (!header.Texture.IsNull)
                {
                    if (!game.LinkCache.TryResolve<ILandscapeTextureGetter>(swaps?.GetValueOrDefault(header.Texture.FormKey,header.Texture.FormKey) ?? header.Texture.FormKey, out var ltex)
                        || ltex.TextureSet?.FormKeyNullable is not { } txKey
                        || !game.LinkCache.TryResolve<ITextureSetGetter>(txKey, out var tx))
                        throw new InvalidDataException($"Unresolved landscape texture {header.Texture.FormKey} in {_world} {coord}.");
                    diffuse = PathOf(tx.Diffuse?.DataRelativePath.Path ?? throw new InvalidDataException("Landscape texture has no diffuse map."));
                    normal = tx.NormalOrGloss is { IsNull: false } n ? PathOf(n.DataRelativePath.Path) : null;
                    if (tx.EnvironmentMaskOrSubsurfaceTint is { IsNull: false } ao) occlusion = PathOf(ao.DataRelativePath.Path);
                }
                float[]? opacity = null;
                if (layer is IAlphaLayerGetter alpha)
                {
                    opacity = new float[17 * 17];
                    foreach (var a in alpha.AlphaLayerData ?? [])
                    {
                        if (a.Position >= opacity.Length || !float.IsFinite(a.Opacity)) throw new InvalidDataException("Invalid LAND alpha data.");
                        opacity[a.Position] = Math.Clamp(a.Opacity, 0, 1);
                    }
                }
                layers.Add(new(diffuse, normal, (int)header.Quadrant, header.LayerNumber, opacity, occlusion));
            }
            Vector3[]? colors = null;
            if (ctx.Record.VertexColors is { } vclr)
            {
                colors = new Vector3[33*33];
                for(int y=0;y<33;y++) for(int x=0;x<33;x++) { var c=vclr[x,y]; colors[y*33+x]=new Vector3(c.X,c.Y,c.Z)/255; }
            }
            Cells[coord] = new(h, colors, layers.OrderBy(l=>l.Order).ToArray()); Heights.Set(_world, coord.X, coord.Y, h);
        }
        foreach (var ctx in game.LoadOrder.PriorityOrder.Cell().WinningContextOverrides(game.LinkCache))
        {
            ct.ThrowIfCancellationRequested();
            var cell = ctx.Record;
            if (cell.IsDeleted || cell.Grid is not { } point 
                || !ctx.TryGetParentSimpleContext<IWorldspaceGetter>(out var ws) || ws.Record.FormKey != world.FormKey) continue;
            _knownCells.Add(new(point.Point.X, point.Point.Y));
            if (!cell.Flags.HasFlag(Mutagen.Bethesda.Skyrim.Cell.Flag.HasWater)) continue;
            float? value = cell.WaterHeight;
            if (value is null || !float.IsFinite(value.Value) || Math.Abs(value.Value) > 1_000_000) value = world.LandDefaults?.DefaultWaterHeight;
            if (value is { } h && float.IsFinite(h) && Math.Abs(h) < 1_000_000) _water[new(point.Point.X, point.Point.Y)] = h;
        }
    }
    private static string PathOf(string path)
    { path = GamePath.Normalize(path); return path.StartsWith("textures\\", StringComparison.Ordinal) ? path : "textures\\" + path; }

    internal IEnumerable<QuadKey> Tiles(IReadOnlyCollection<LodLevel> levels)
    {
        var tiles = new HashSet<QuadKey>();
        foreach (var c in Cells.Keys) foreach (var level in levels) tiles.Add(_grid.QuadFor(c,level));
        if (_fallback)
            foreach (string path in _assets.EnumeratePaths($@"meshes\terrain\{_world}\"))
            {
                var parts = GamePath.GetFileName(path).Split('.');
                if (parts.Length == 5 && parts[4] == "btr" && parts[0].Equals(_world, StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(parts[1],out int level) && levels.Contains((LodLevel)level)
                    && int.TryParse(parts[2],out int x) && int.TryParse(parts[3],out int y) && _grid.IsInsideGrid(new(x,y)))
                    tiles.Add(new(_world,(LodLevel)level,x,y));
            }
        return tiles.OrderBy(t=>(int)t.Level).ThenBy(t=>t.X).ThenBy(t=>t.Y);
    }
    internal float? Height(float x,float y)
    {
        float gx=x/128, gy=y/128; int ix=(int)MathF.Floor(gx), iy=(int)MathF.Floor(gy);
        float fx=gx-ix, fy=gy-iy;
        float? Vertex(int a,int b)
        {
            var land=LandVertex(a,b);if(land is not null || !_fallback)return land;
            if(_fallbackVertices.TryGetValue((a,b),out var known))return known;
            // High-resolution texture pixels repeatedly interpolate the same 128-unit terrain vertices.
            if(_fallbackVertices.Count>=65536)_fallbackVertices.Clear();
            return _fallbackVertices[(a,b)]=FallbackHeight(a*128f,b*128f);
        }
        var a=Vertex(ix,iy); if(a is null) return null;
        var b=fx<.00001f ? a : Vertex(ix+1,iy); var c=fy<.00001f ? a : Vertex(ix,iy+1);
        var d=fy<.00001f ? b : fx<.00001f ? c : Vertex(ix+1,iy+1);
        if(b is null || c is null || d is null) return null;
        return (a.Value*(1-fx)+b.Value*fx)*(1-fy)+(c.Value*(1-fx)+d.Value*fx)*fy;
    }
    private float? LandVertex(int gx,int gy)
    {
        int cx=gx>>5,cy=gy>>5; // signed shift is floor division by 32, including negative coordinates
        if(Cells.TryGetValue(new(cx,cy),out var cell))return cell.Heights[(gy-cy*32)*33+gx-cx*32];
        // Match TerrainHeights.VertexAt's north/east ownership, including the outermost LAND border.
        for(int dy=0;dy>=-1;dy--)for(int dx=0;dx>=-1;dx--)
            if((dx==0||gx%32==0)&&(dy==0||gy%32==0)&&Cells.TryGetValue(new(cx+dx,cy+dy),out cell))
                return cell.Heights[(gy-(cy+dy)*32)*33+gx-(cx+dx)*32];
        return null;
    }
    private float? FallbackHeight(float x,float y, bool water = false)
    {
        if (!_fallback) return null;
        foreach(var level in TerrainLevels)
        {
            var center = _grid.QuadFor(CellCoord.FromWorld(x,y),level);
            // At the grid's east/north limit, try the tile on the other side of the shared edge.
            for(int neighbor=0;neighbor<4;neighbor++)
            {
                var tile=center with {X=center.X-(neighbor%2)*(int)level,Y=center.Y-(neighbor/2)*(int)level};
                float lx=x-tile.X*4096f, ly=y-tile.Y*4096f;
                if(lx<0 || ly<0 || lx>(int)level*4096 || ly>(int)level*4096) continue;
                if(!_meshes.TryGetValue(tile,out var field))
                {
                    if(_meshes.Count>=16) { _meshes.Clear(); _waterMeshes.Clear(); }
                    string path=$@"meshes\terrain\{_world}\{_world}.{(int)level}.{tile.X}.{tile.Y}.btr";
                    field=null;
                    if(_assets.TryOpen(path,out var stream))
                    {
                        using(stream) { using var ms=new MemoryStream(); stream.CopyTo(ms); var mesh=NifGeometryReader.Read(path,ms.ToArray(),passthru:true,includeUnshaded:true);
                            if(mesh.Parts.Count==0) throw new InvalidDataException("Existing terrain cannot be read: "+path);
                            _waterMeshes[tile]=new TriangleField(mesh.Parts.Where(p=>ReferenceEquals(p.Material,NifGeometryReader.UnshadedMaterial)).SelectMany(p=>Enumerable.Range(0,p.Triangles.Length/3).Select(i=>(p.Positions[p.Triangles[i*3]],p.Positions[p.Triangles[i*3+1]],p.Positions[p.Triangles[i*3+2]]))),(int)level*4096f);
                            field=new TriangleField(mesh.Parts.Where(p=>!ReferenceEquals(p.Material,NifGeometryReader.UnshadedMaterial)).SelectMany(p=>Enumerable.Range(0,p.Triangles.Length/3).Select(i=>(p.Positions[p.Triangles[i*3]],p.Positions[p.Triangles[i*3+1]],p.Positions[p.Triangles[i*3+2]]))),(int)level*4096f); }
                    }
                    _meshes[tile]=field;
                }
                if((water ? _waterMeshes.GetValueOrDefault(tile) : field)?.Sample(lx,ly) is {} h) { _fallbackUsed.Add(tile); return h; }
            }
        }
        return null;
    }
    internal float? Water(int x,int y)
    {
        if(!_water.TryGetValue(new(x,y),out float h))
            return _knownCells.Contains(new(x,y)) ? null : FallbackHeight((x+.5f)*4096,(y+.5f)*4096,water:true);
        // A water-flagged cell can be entirely above water; avoid placing exposed water over it.
        for(int iy=0;iy<=32;iy++) for(int ix=0;ix<=32;ix++)
            if(Height(x*4096f+ix*128,y*4096f+iy*128) is {} ground && ground<h) return h;
        return null;
    }
    internal Vector3 Color(float x,float y,float footprint,float repeats,float colorStrength) => Surface(x,y,footprint,repeats,colorStrength).Color;
    internal TerrainSample Surface(float x,float y,float footprint,float repeats,float colorStrength)
    {
        var coord=CellCoord.FromWorld(x,y);
        if(!Cells.TryGetValue(coord,out var cell))
        {
            if(_fallback) foreach(var level in TerrainLevels)
            {
                var tile=_grid.QuadFor(coord,level); string path=$@"textures\terrain\{_world}\{_world}.{(int)level}.{tile.X}.{tile.Y}.dds";
                if (_season is not null && _assets.Exists(path[..^4] + "." + _season + ".dds")) path = path[..^4] + "." + _season + ".dds";
                if(!_assets.Exists(path))continue;
                float u=(x/4096-tile.X)/(int)level, v=1-(y/4096-tile.Y)/(int)level;
                Vector3? existingNormal = null;
                if (_materialSettings.NormalStrength > 0 && _materials.Optional(path[..^4] + "_n.dds", "normal", out var npath))
                {
                    var n = _materials.Read(npath,u,v,footprint/((int)level*4096),false,false)*2-Vector3.One;
                    if (n.LengthSquared() > 1e-8f) existingNormal = Vector3.Normalize(n);
                }
                return new(_materials.Read(path,u,v,footprint/((int)level*4096),true,false),Vector3.UnitZ,existingNormal);
            }
            throw new InvalidDataException($"Missing landscape texture coverage in {_world} {coord}.");
        }
        float vx=(x-coord.X*4096)/128, vy=(y-coord.Y*4096)/128;
        int quadrant=(vx>=16?1:0)+(vy>=16?2:0);
        float qx=vx%16,qy=vy%16;
        var baseLayer=cell.Bases[quadrant];
        float total=0; Vector3 color=Vector3.Zero, detail=Vector3.Zero;
        foreach(var layer in cell.Alpha[quadrant])
        {
            float weight=Bilinear(layer.Opacity!,17,qx,qy);
            if(weight>0) { var sample=Material(layer); total+=weight; color+=sample.Color*weight; detail+=sample.Detail*weight; }
        }
        float baseWeight = Math.Max(0, 1 - total);
        if (baseWeight > 0) { var sample=Material(baseLayer); color+=sample.Color*baseWeight; detail+=sample.Detail*baseWeight; }
        color/=Math.Max(1,total);
        if(cell.Colors is {} colors)
        {
            int ix=Math.Clamp((int)vx,0,31),iy=Math.Clamp((int)vy,0,31); float fx=vx-ix,fy=vy-iy;
            var v=Vector3.Lerp(Vector3.Lerp(colors[iy*33+ix],colors[iy*33+ix+1],fx),Vector3.Lerp(colors[(iy+1)*33+ix],colors[(iy+1)*33+ix+1],fx),fy);
            color*=Vector3.Lerp(Vector3.One,v,colorStrength);
        }
        return new(color, detail / Math.Max(1,total));
        TerrainSample Material(Layer? layer) {
            string path=layer?.Texture ?? _defaultDiffuse;
            try { return _materials.Sample(path,layer is null ? _defaultNormal : layer.Normal,layer?.Occlusion,
                x/4096*repeats,-y/4096*repeats,footprint*repeats/4096); }
            catch (FileNotFoundException e) { throw new FileNotFoundException($"Missing landscape texture: {e.FileName ?? path} (world {_world}, cell {coord.X},{coord.Y}, quadrant {quadrant}).", e.FileName ?? path, e); }
        }
    }
    internal static Vector3 Sample(TextureImage image,float u,float v,bool wrap)
    {
        float x=u*image.Width-.5f,y=v*image.Height-.5f;int ix=(int)MathF.Floor(x),iy=(int)MathF.Floor(y);float fx=x-ix,fy=y-iy;
        Vector3 Pixel(int px,int py) {px=wrap?(px%image.Width+image.Width)%image.Width:Math.Clamp(px,0,image.Width-1);py=wrap?(py%image.Height+image.Height)%image.Height:Math.Clamp(py,0,image.Height-1);
            int o=(py*image.Width+px)*4;return new Vector3(image.Pixels[o],image.Pixels[o+1],image.Pixels[o+2])/255;}
        return Vector3.Lerp(Vector3.Lerp(Pixel(ix,iy),Pixel(ix+1,iy),fx),Vector3.Lerp(Pixel(ix,iy+1),Pixel(ix+1,iy+1),fx),fy);
    }
    internal static float Bilinear(float[] values,int n,float x,float y)
    { int ix=Math.Clamp((int)x,0,n-2),iy=Math.Clamp((int)y,0,n-2);float fx=Math.Clamp(x-ix,0,1),fy=Math.Clamp(y-iy,0,1);
        return (values[iy*n+ix]*(1-fx)+values[iy*n+ix+1]*fx)*(1-fy)+(values[(iy+1)*n+ix]*(1-fx)+values[(iy+1)*n+ix+1]*fx)*fy; }
    internal static Vector3 SrgbToLinear(Vector3 c)=>new(Linear(c.X),Linear(c.Y),Linear(c.Z));
    private static float Linear(float c)=>c<=.04045f?c/12.92f:MathF.Pow((c+.055f)/1.055f,2.4f);
    internal static byte Srgb(float c) =>(byte)Math.Clamp(MathF.Round((c<=.0031308f?12.92f*c:1.055f*MathF.Pow(c,1/2.4f)-.055f)*255),0,255);

    private sealed class TriangleField
    {
        private readonly List<(Vector3 A,Vector3 B,Vector3 C)>[] _buckets=Enumerable.Range(0,32*32).Select(_=>new List<(Vector3,Vector3,Vector3)>()).ToArray();
        private readonly float _size;
        public TriangleField(IEnumerable<(Vector3 A,Vector3 B,Vector3 C)> faces,float span)
        {
            _size=span/32;
            foreach(var f in faces)
            {
                int x0=Math.Clamp((int)(Math.Min(f.A.X,Math.Min(f.B.X,f.C.X))/_size),0,31),x1=Math.Clamp((int)(Math.Max(f.A.X,Math.Max(f.B.X,f.C.X))/_size),0,31);
                int y0=Math.Clamp((int)(Math.Min(f.A.Y,Math.Min(f.B.Y,f.C.Y))/_size),0,31),y1=Math.Clamp((int)(Math.Max(f.A.Y,Math.Max(f.B.Y,f.C.Y))/_size),0,31);
                for(int y=y0;y<=y1;y++) for(int x=x0;x<=x1;x++)_buckets[y*32+x].Add(f);
            }
        }
        public float? Sample(float x,float y)
        {
            foreach(var (a,b,c) in _buckets[Math.Clamp((int)(y/_size),0,31)*32+Math.Clamp((int)(x/_size),0,31)])
            {
                float den=(b.Y-c.Y)*(a.X-c.X)+(c.X-b.X)*(a.Y-c.Y);if(Math.Abs(den)<.001f)continue;
                float u=((b.Y-c.Y)*(x-c.X)+(c.X-b.X)*(y-c.Y))/den,v=((c.Y-a.Y)*(x-c.X)+(a.X-c.X)*(y-c.Y))/den;
                if(u>=-.0001f && v>=-.0001f && u+v<=1.0001f)return u*a.Z+v*b.Z+(1-u-v)*c.Z;
            }
            return null;
        }
    }
}
