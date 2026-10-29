namespace AnvilLOD.Plugins;

/// <summary>Static terrain texture calibration; runtime PBR lighting remains the game's responsibility.</summary>
public sealed record TerrainMaterialSettings(float NormalStrength = 1, bool PbrAlbedo = true,
    bool PbrTwins = false, float PbrScale = .65f, float PbrGamma = 1.15f, float OcclusionStrength = .5f)
{
    public void Validate()
    {
        if (!float.IsFinite(NormalStrength) || NormalStrength is < 0 or > 2
            || !float.IsFinite(PbrScale) || PbrScale is < .1f or > 4
            || !float.IsFinite(PbrGamma) || PbrGamma is < .1f or > 4
            || !float.IsFinite(OcclusionStrength) || OcclusionStrength is < 0 or > 1)
            throw new ArgumentException("Invalid terrain normal or PBR calibration.");
    }
}

public sealed record TerrainMaterialReport(string Source, string Diffuse, string? Normal, string? Occlusion, bool Pbr);
