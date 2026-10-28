using System.Runtime.InteropServices;
using System.Text.Json;
using AnvilLOD.Core.World;
using AnvilLOD.Desktop.Services;
using AnvilLOD.Plugins;
using AnvilLOD.Textures;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace AnvilLOD.Desktop;

public sealed partial class MainWindow
{
    private readonly DataGrid _textureGenResults = Table(("Kind", "Kind"), ("Output", "Output"), ("Status", "Status"), ("Details", "Detail"));
    private readonly TextBlock _textureGenSummary = Text("No textures generated in this session.", true);
    private TextureGenerationResult? _lastTextureGeneration;

    private Control TextureGenerationPage() => Scroll(Stack(16,
        Heading("Texture generation", "Generate LOD textures natively from the load order selected in Overview."),
        Card("INPUT AND OUTPUT", Stack(12,
            PathField("DynDOLOD installation / resources", nameof(_settings.DynDolodFolder), hint: "Reads installed stitching recipes and trunk/object render models. No Windows executable is launched."),
            PathField("Texture output mod folder", nameof(_settings.TextureGenOutput), hint: "Use a dedicated folder outside your game and enabled mods, separate from LOD output."),
            Check("Use this generated texture output for subsequent LOD jobs", nameof(_settings.UseGeneratedTextures)),
            Text("Generate textures, then install both the texture output and the LOD output as mods. The combined job reads new textures automatically; LOD output should win conflicts.", true))),
        Card("TEXTURES TO GENERATE", Stack(10,
            Row(Check("Stitched object textures", nameof(_settings.TextureGenStitched)), Check("Tree billboards", nameof(_settings.TextureGenTrees)), Check("Grass billboards", nameof(_settings.TextureGenGrass))),
            Check("Hybrid tree trunk textures", nameof(_settings.TextureGenTrunks)),
            Text("Renders front and side trunks for installed hybrid tree LOD models. Their existing cards set the crop and alignment. Enable 3D trees in Generation settings, then run Textures, then LOD. Farther levels still use tree billboards.", true),
            Check("Rendered object textures (experimental)", nameof(_settings.TextureGenObjects)),
            Text("Grass billboards follow the current landscape grass lists. Optional object templates update installed LOD textures; when rendering is unsupported, a valid existing texture pair is retained and listed in the results.", true),
            Text("Produces BC7 diffuse and geometry-normal maps. Grass and trunks need their models’ source diffuse textures. Hybrid trees require matching render resources and prebuilt 3D LOD meshes. PBR shader baking, specialized glow/alpha conversion rules, HD billboards and seasonal billboard variants are not implemented. Keep TexGen output for those features.", true))),
        Card("QUALITY", Stack(12,
            Row(TextureSizeChoice("Stitch base size", nameof(_settings.TextureGenBaseSize), [64, 128, 256, 512, 1024]),
                TextureSizeChoice("Maximum billboard size", nameof(_settings.TextureGenMaxSize), [32, 64, 128, 256, 512, 1024, 2048])),
            Text("Sizes must be powers of two: 64, 128, 256, 512… Rendered objects use the base size as their resolution scale.", true),
            Row(Number("World units per pixel", nameof(_settings.TextureGenUnitsPerPixel), .25m, 128, .25m), Number("Brightness multiplier", nameof(_settings.TextureGenBrightness), .1m, 4, .05m)),
            Row(Number("Minimum tree height", nameof(_settings.TextureGenMinTreeHeight), 0, 65536, 16), Number("Minimum grass height", nameof(_settings.TextureGenMinGrassHeight), 0, 65536, 4)),
            Field("Model path contains (optional)", nameof(_settings.TextureGenFilter), "Limits billboards and rendered objects for previews. Clear this for a full run; stitching always processes all matching recipes."))),
        Row(Primary("Generate textures", () => RunTextureGeneration(false)), Button("Textures, then LOD", () => RunTextureGeneration(true)),
            Button("Open texture output", () => Open(RequestFactory.RequiredPath(_settings.TextureGenOutput, "a texture output folder", _settings)))),
        Card("RESULTS", Stack(12, _textureGenSummary, _textureGenResults, Button("Preview selected texture", PreviewGeneratedTexture)))));

    private Control TextureSizeChoice(string label, string property, decimal[] sizes)
    {
        var input = new ComboBox { Name = property, ItemsSource = sizes, MinWidth = 160 };
        Bind(input, SelectingItemsControl.SelectedItemProperty, _settings, property);
        return Labeled(label, input);
    }

    internal Task RunTextureGeneration(bool thenLod)
    {
        var snapshot = _settings.Snapshot();
        return RunPersistentPipeline(thenLod ? "Textures and LOD" : "Texture generation", snapshot,
            s => new GenerationPipelineRequest(Textures: RequestFactory.Textures(s),
                Lod: thenLod ? RequestFactory.Scan(s, true) : null,
                Resume: s.ResumeCompletedStages, VerifySourceContents: s.VerifySourceContents));
    }

    private void PreviewGeneratedTexture()
    {
        if (_lastTextureGeneration is not { Published: true } result || _textureGenResults.SelectedItem is not TextureGenerationItem { Status: "Written" or "Retained" } item)
            throw new InvalidOperationException("Select a written texture from a successful run first.");
        var decoded = TextureImages.Decode(File.ReadAllBytes(GamePath.ToFileSystemPath(result.OutputFolder, item.Output)));
        var bitmap = new WriteableBitmap(new PixelSize(decoded.Width, decoded.Height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using (var buffer = bitmap.Lock())
            for (int y = 0; y < decoded.Height; y++) Marshal.Copy(decoded.Pixels, y * decoded.Width * 4, buffer.Address + y * buffer.RowBytes, decoded.Width * 4);
        var preview = new Window
        {
            Title = $"{item.Output} · {decoded.Width} × {decoded.Height}", Width = 760, Height = 650,
            Background = Brush.Parse("#343b44"), Content = new Image { Source = bitmap, Stretch = Stretch.Uniform, Margin = new Thickness(20) }
        };
        preview.Closed += (_, _) => bitmap.Dispose();
        preview.Show(this);
    }
}
