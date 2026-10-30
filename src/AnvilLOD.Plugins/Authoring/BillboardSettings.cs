using System.Numerics;
using AnvilLOD.Textures;

namespace AnvilLOD.Plugins.Authoring;

public enum GrassTextureMode { Auto, Basic, Packed }
public enum BillboardView { Front, Side }

public sealed record BillboardSettings(bool HdViews = true, bool ReferenceScale = true,
    float UnplacedTreeScale = 1, GrassTextureMode GrassTextures = GrassTextureMode.Auto, float ComplexGrassThreshold = .1f)
{
    public void Validate()
    {
        if (!float.IsFinite(UnplacedTreeScale) || UnplacedTreeScale is <= 0 or > 100
            || !Enum.IsDefined(GrassTextures) || !float.IsFinite(ComplexGrassThreshold) || ComplexGrassThreshold is <= 0 or > .5f)
            throw new ArgumentException("Invalid billboard scale or complex-grass detection settings.");
    }
}

/// <summary>Complex grass stores albedo/opacity above tangent normals/specular in one DDS.
/// Split before filtering so wrapping and mip reduction cannot mix the two halves.</summary>
internal static class GrassTexture
{
    internal static bool IsPacked((byte[] Rgba, int Width, int Height) image, float threshold)
    {
        if (image.Height < 2 || (image.Height & 1) != 0) return false;
        int i = (image.Height - 1) * image.Width * 4;
        var normal = new Vector3(image.Rgba[i], image.Rgba[i + 1], image.Rgba[i + 2]) / 127.5f - Vector3.One;
        return MathF.Abs(normal.Length() - 1) < threshold;
    }

    internal static ((byte[] Rgba, int Width, int Height) Diffuse, (byte[] Rgba, int Width, int Height)? Normal) Decode(
        (byte[] Rgba, int Width, int Height) image, GrassTextureMode mode, float threshold)
    {
        bool packed = mode == GrassTextureMode.Packed || mode == GrassTextureMode.Auto && IsPacked(image, threshold);
        if (!packed) return (Reduce(image, normal: false), null);
        if (image.Height < 2 || (image.Height & 1) != 0)
            throw new InvalidDataException("Packed grass textures need two equal-height image halves.");
        int size = checked(image.Width * (image.Height / 2) * 4);
        var diffuse = image.Rgba.AsSpan(0, size).ToArray();
        var normal = image.Rgba.AsSpan(size, size).ToArray();
        return (Reduce((diffuse, image.Width, image.Height / 2), false), Reduce((normal, image.Width, image.Height / 2), true));
    }

    private static (byte[] Rgba, int Width, int Height) Reduce((byte[] Rgba, int Width, int Height) image, bool normal)
    {
        // Specular alpha must not weight the normal direction during resampling.
        if (normal) for (int i = 3; i < image.Rgba.Length; i += 4) image.Rgba[i] = 255;
        while (image.Width > 1024 || image.Height > 1024)
            image = TreeAtlasBuilder.Downsample(image.Rgba, image.Width, image.Height);
        if (normal) for (int i = 0; i < image.Rgba.Length; i += 4) BillboardRenderer.Renormalize(image.Rgba, i);
        return image;
    }
}
