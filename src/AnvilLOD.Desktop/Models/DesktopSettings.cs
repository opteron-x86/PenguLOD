using System.Text.Json;
using System.Text.Json.Serialization;

namespace AnvilLOD.Desktop.Models;

public sealed class DesktopSettings : ObservableObject
{
    public int SchemaVersion { get; set; } = 1;
    public Dictionary<string, JsonElement> Options { get; set; } = [];
    private string _sourceMode = "Mod Organizer 2";
    public string SourceMode { get => _sourceMode; set => Set(ref _sourceMode, value ?? ""); }
    private string _edition = "SE / AE";
    public string Edition { get => _edition; set => Set(ref _edition, value ?? "SE / AE"); }
    private string _instance = "";
    public string Instance { get => _instance; set => Set(ref _instance, value ?? ""); }
    private string _profile = "";
    public string Profile { get => _profile; set => Set(ref _profile, value ?? ""); }
    private string _winePrefix = "";
    public string WinePrefix { get => _winePrefix; set => Set(ref _winePrefix, value ?? ""); }
    private string _gameFolder = "";
    public string GameFolder { get => _gameFolder; set => Set(ref _gameFolder, value ?? ""); }
    private string _modsFolder = "";
    public string ModsFolder { get => _modsFolder; set => Set(ref _modsFolder, value ?? ""); }
    private string _profilesFolder = "";
    public string ProfilesFolder { get => _profilesFolder; set => Set(ref _profilesFolder, value ?? ""); }
    private string _overwriteFolder = "";
    public string OverwriteFolder { get => _overwriteFolder; set => Set(ref _overwriteFolder, value ?? ""); }
    private string _pluginsFile = "";
    public string PluginsFile { get => _pluginsFile; set => Set(ref _pluginsFile, value ?? ""); }
    private string _gameIni = "";
    public string GameIni { get => _gameIni; set => Set(ref _gameIni, value ?? ""); }
    private string _outputFolder = "";
    public string OutputFolder { get => _outputFolder; set => Set(ref _outputFolder, value ?? ""); }
    private string _dynDolodFolder = "";
    public string DynDolodFolder { get => _dynDolodFolder; set => Set(ref _dynDolodFolder, value ?? ""); }
    private string _customRulesFile = "";
    public string CustomRulesFile { get => _customRulesFile; set => Set(ref _customRulesFile, value ?? ""); }
    private string _worldspaces = "Tamriel";
    public string Worldspaces { get => _worldspaces; set => Set(ref _worldspaces, value ?? ""); }
    private string _cacheFolder = "";
    public string CacheFolder { get => _cacheFolder; set => Set(ref _cacheFolder, value ?? ""); }
    private string _makerInputs = "";
    public string MakerInputs { get => _makerInputs; set => Set(ref _makerInputs, value ?? ""); }
    private string _makerOutput = "";
    public string MakerOutput { get => _makerOutput; set => Set(ref _makerOutput, value ?? ""); }
    private string _makerGroup = "AnvilLOD";
    public string MakerGroup { get => _makerGroup; set => Set(ref _makerGroup, value ?? ""); }
    private string _makerPlugin = "";
    public string MakerPlugin { get => _makerPlugin; set => Set(ref _makerPlugin, value ?? ""); }
    private string _makerLevels = "0,1,2";
    public string MakerLevels { get => _makerLevels; set => Set(ref _makerLevels, value ?? ""); }
    private string _makerBudget = "Recommended";
    public string MakerBudget { get => _makerBudget; set => Set(ref _makerBudget, value ?? ""); }
    private string _makerCustomBudget = "800,500,300";
    public string MakerCustomBudget { get => _makerCustomBudget; set => Set(ref _makerCustomBudget, value ?? ""); }
    private string _authorPlugin = "*";
    public string AuthorPlugin { get => _authorPlugin; set => Set(ref _authorPlugin, value ?? ""); }
    private string _authorOutput = "";
    public string AuthorOutput { get => _authorOutput; set => Set(ref _authorOutput, value ?? ""); }
    private bool _pipelinePreflight = true;
    public bool PipelinePreflight { get => _pipelinePreflight; set => Set(ref _pipelinePreflight, value); }
    private bool _diagnosticDeepScan;
    public bool DiagnosticDeepScan { get => _diagnosticDeepScan; set => Set(ref _diagnosticDeepScan, value); }
    private bool _diagnosticVerifyFiles;
    public bool DiagnosticVerifyFiles { get => _diagnosticVerifyFiles; set => Set(ref _diagnosticVerifyFiles, value); }
    private string _diagnosticAsset = "";
    public string DiagnosticAsset { get => _diagnosticAsset; set => Set(ref _diagnosticAsset, value); }
    private string _terrainOutput = "";
    public string TerrainOutput { get => _terrainOutput; set => Set(ref _terrainOutput, value); }
    private bool _pipelineTerrain = false;
    public bool PipelineTerrain { get => _pipelineTerrain; set => Set(ref _pipelineTerrain, value); }
    private bool _useGeneratedTerrain = false;
    public bool UseGeneratedTerrain { get => _useGeneratedTerrain; set => Set(ref _useGeneratedTerrain, value); }
    private decimal _terrainTextureSize = 512;
    public decimal TerrainTextureSize { get => _terrainTextureSize; set => Set(ref _terrainTextureSize, value); }
    private decimal _terrainBrightness = 1;
    public decimal TerrainBrightness { get => _terrainBrightness; set => Set(ref _terrainBrightness, value); }
    private decimal _terrainVertexColor = 1;
    public decimal TerrainVertexColor { get => _terrainVertexColor; set => Set(ref _terrainVertexColor, value); }
    private decimal _terrainRepeats = 24;
    public decimal TerrainRepeats { get => _terrainRepeats; set => Set(ref _terrainRepeats, value); }
    private decimal _terrainSkirt = 512;
    public decimal TerrainSkirt { get => _terrainSkirt; set => Set(ref _terrainSkirt, value); }
    private bool _terrainWater = true;
    public bool TerrainWater { get => _terrainWater; set => Set(ref _terrainWater, value); }
    private bool _terrainFallback = true;
    public bool TerrainFallback { get => _terrainFallback; set => Set(ref _terrainFallback, value); }
    private bool _terrainHeightmaps = true;
    public bool TerrainHeightmaps { get => _terrainHeightmaps; set => Set(ref _terrainHeightmaps, value); }
    private decimal _terrainHeightmapSize = 8192;
    public decimal TerrainHeightmapSize { get => _terrainHeightmapSize; set => Set(ref _terrainHeightmapSize, value); }
    private string _textureGenOutput = "";
    public string TextureGenOutput { get => _textureGenOutput; set => Set(ref _textureGenOutput, value ?? ""); }
    private string _textureGenFilter = "";
    public string TextureGenFilter { get => _textureGenFilter; set => Set(ref _textureGenFilter, value ?? ""); }
    private bool _textureGenStitched = true;
    public bool TextureGenStitched { get => _textureGenStitched; set => Set(ref _textureGenStitched, value); }
    private bool _textureGenTrees = true;
    public bool TextureGenTrees { get => _textureGenTrees; set => Set(ref _textureGenTrees, value); }
    private bool _textureGenTrunks = true;
    public bool TextureGenTrunks { get => _textureGenTrunks; set => Set(ref _textureGenTrunks, value); }
    private bool _textureGenGrass = false;
    public bool TextureGenGrass { get => _textureGenGrass; set => Set(ref _textureGenGrass, value); }
    private bool _textureGenObjects = false;
    public bool TextureGenObjects { get => _textureGenObjects; set => Set(ref _textureGenObjects, value); }
    private bool _useGeneratedTextures = false;
    public bool UseGeneratedTextures { get => _useGeneratedTextures; set => Set(ref _useGeneratedTextures, value); }
    private decimal _textureGenBaseSize = 256m;
    public decimal TextureGenBaseSize { get => _textureGenBaseSize; set => Set(ref _textureGenBaseSize, value); }
    private decimal _textureGenMaxSize = 512m;
    public decimal TextureGenMaxSize { get => _textureGenMaxSize; set => Set(ref _textureGenMaxSize, value); }
    private decimal _textureGenUnitsPerPixel = 12m;
    public decimal TextureGenUnitsPerPixel { get => _textureGenUnitsPerPixel; set => Set(ref _textureGenUnitsPerPixel, value); }
    private decimal _textureGenBrightness = 1m;
    private bool _bakeSourceNormals = true, _bakePbr = true;
    private decimal _bakePbrScale = .65m, _bakePbrGamma = 1.15m, _bakeAo = .5m, _bakeAmbient = .7m, _bakeDirectional = .3m;
    public bool BakeSourceNormals { get => _bakeSourceNormals; set => Set(ref _bakeSourceNormals, value); }
    public bool BakePbr { get => _bakePbr; set => Set(ref _bakePbr, value); }
    public decimal BakePbrScale { get => _bakePbrScale; set => Set(ref _bakePbrScale, value); }
    public decimal BakePbrGamma { get => _bakePbrGamma; set => Set(ref _bakePbrGamma, value); }
    public decimal BakeAo { get => _bakeAo; set => Set(ref _bakeAo, value); }
    public decimal BakeAmbient { get => _bakeAmbient; set => Set(ref _bakeAmbient, value); }
    public decimal BakeDirectional { get => _bakeDirectional; set => Set(ref _bakeDirectional, value); }
    public decimal TextureGenBrightness { get => _textureGenBrightness; set => Set(ref _textureGenBrightness, value); }
    private decimal _textureGenMinTreeHeight = 256m;
    public decimal TextureGenMinTreeHeight { get => _textureGenMinTreeHeight; set => Set(ref _textureGenMinTreeHeight, value); }
    private decimal _textureGenMinGrassHeight = 16m;
    public decimal TextureGenMinGrassHeight { get => _textureGenMinGrassHeight; set => Set(ref _textureGenMinGrassHeight, value); }
    private string _grassOutput = "";
    public string GrassOutput { get => _grassOutput; set => Set(ref _grassOutput, value ?? ""); }
    private string _grassExecutable = "";
    public string GrassExecutable { get => _grassExecutable; set => Set(ref _grassExecutable, value ?? ""); }
    private string _grassWorldspaces = "all";
    public string GrassWorldspaces { get => _grassWorldspaces; set => Set(ref _grassWorldspaces, value ?? ""); }
    private string _grassPlacement = "Smooth";
    public string GrassPlacement { get => _grassPlacement; set => Set(ref _grassPlacement, value ?? "Smooth"); }
    private string _grassRejection = "Auto";
    public string GrassRejection { get => _grassRejection; set => Set(ref _grassRejection, value ?? "Auto"); }
    private string _grassSeasons = "Auto";
    public string GrassSeasons { get => _grassSeasons; set => Set(ref _grassSeasons, value ?? "Auto"); }
    private string _grassNgioConfig = "";
    public string GrassNgioConfig { get => _grassNgioConfig; set => Set(ref _grassNgioConfig, value ?? ""); }
    private bool _grassPreview;
    public bool GrassPreview { get => _grassPreview; set => Set(ref _grassPreview, value); }
    private bool _grassRenderGeometry;
    public bool GrassRenderGeometry { get => _grassRenderGeometry; set => Set(ref _grassRenderGeometry, value); }
    private bool _useGeneratedGrass;
    public bool UseGeneratedGrass { get => _useGeneratedGrass; set => Set(ref _useGeneratedGrass, value); }
    private decimal _grassCellX;
    public decimal GrassCellX { get => _grassCellX; set => Set(ref _grassCellX, value); }
    private decimal _grassCellY;
    public decimal GrassCellY { get => _grassCellY; set => Set(ref _grassCellY, value); }
    private decimal _grassRadius = 1;
    public decimal GrassRadius { get => _grassRadius; set => Set(ref _grassRadius, value); }
    private decimal _grassWriters = 1;
    public decimal GrassWriters { get => _grassWriters; set => Set(ref _grassWriters, value); }
    private decimal _grassMinSize;
    public decimal GrassMinSize { get => _grassMinSize; set => Set(ref _grassMinSize, value); }
    private string _textureInputs = "";
    public string TextureInputs { get => _textureInputs; set => Set(ref _textureInputs, value ?? ""); }
    private string _textureOutput = "";
    public string TextureOutput { get => _textureOutput; set => Set(ref _textureOutput, value ?? ""); }
    private string _texconvPath = "";
    public string TexconvPath { get => _texconvPath; set => Set(ref _texconvPath, value ?? ""); }
    private string _convertSource = "";
    public string ConvertSource { get => _convertSource; set => Set(ref _convertSource, value ?? ""); }
    private string _convertOutput = "";
    public string ConvertOutput { get => _convertOutput; set => Set(ref _convertOutput, value ?? ""); }
    private bool _cleanOutput = false;
    public bool CleanOutput { get => _cleanOutput; set => Set(ref _cleanOutput, value); }
    private bool _makerOverwrite = false;
    public bool MakerOverwrite { get => _makerOverwrite; set => Set(ref _makerOverwrite, value); }
    private bool _makerRules = true;
    public bool MakerRules { get => _makerRules; set => Set(ref _makerRules, value); }
    private bool _authorMeshes = true;
    public bool AuthorMeshes { get => _authorMeshes; set => Set(ref _authorMeshes, value); }
    private bool _authorBillboards = true;
    public bool AuthorBillboards { get => _authorBillboards; set => Set(ref _authorBillboards, value); }
    private bool _authorRules = true;
    public bool AuthorRules { get => _authorRules; set => Set(ref _authorRules, value); }
    private bool _authorStandIns = false;
    public bool AuthorStandIns { get => _authorStandIns; set => Set(ref _authorStandIns, value); }
    private bool _authorOverwrite = false;
    public bool AuthorOverwrite { get => _authorOverwrite; set => Set(ref _authorOverwrite, value); }
    private bool _textureAllMods = false;
    public bool TextureAllMods { get => _textureAllMods; set => Set(ref _textureAllMods, value); }
    private bool _convertAllMeshes = false;
    public bool ConvertAllMeshes { get => _convertAllMeshes; set => Set(ref _convertAllMeshes, value); }
    private bool _convertOverwrite = false;
    public bool ConvertOverwrite { get => _convertOverwrite; set => Set(ref _convertOverwrite, value); }
    private decimal _maxThreads = 0m;
    public decimal MaxThreads { get => _maxThreads; set => Set(ref _maxThreads, value); }
    private decimal _makerDetail = 1m;
    public decimal MakerDetail { get => _makerDetail; set => Set(ref _makerDetail, value); }
    private decimal _makerMinSize = 0m;
    public decimal MakerMinSize { get => _makerMinSize; set => Set(ref _makerMinSize, value); }
    private decimal _authorMinSize = 400m;
    public decimal AuthorMinSize { get => _authorMinSize; set => Set(ref _authorMinSize, value); }
    private decimal _authorTreeHeight = 256m;
    public decimal AuthorTreeHeight { get => _authorTreeHeight; set => Set(ref _authorTreeHeight, value); }

    private bool _resumeCompletedStages = true;
    public bool ResumeCompletedStages { get => _resumeCompletedStages; set => Set(ref _resumeCompletedStages, value); }
    private bool _verifySourceContents;
    public bool VerifySourceContents { get => _verifySourceContents; set => Set(ref _verifySourceContents, value); }
    private bool _pipelineGrass = true;
    public bool PipelineGrass { get => _pipelineGrass; set => Set(ref _pipelineGrass, value); }
    private bool _pipelineTextures = true;
    public bool PipelineTextures { get => _pipelineTextures; set => Set(ref _pipelineTextures, value); }
    private bool _pipelineLod = true;
    public bool PipelineLod { get => _pipelineLod; set => Set(ref _pipelineLod, value); }

    public DesktopSettings Snapshot() => JsonSerializer.Deserialize<DesktopSettings>(JsonSerializer.Serialize(this))!;
}
