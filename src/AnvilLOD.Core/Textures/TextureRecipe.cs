using System.Globalization;
using AnvilLOD.Core.World;

namespace AnvilLOD.Core.Textures;

public sealed record TextureTile(string Source, string Destination, float Width, float Height, float X, float Y,
    float AtlasWidth, float AtlasHeight, int Brightness, float Red, float Green, float Blue, bool Alpha);
public sealed record TextureCopy(string Source, string Destination);
public sealed record TextureConversion(string Source, string Destination, int Filter, float Contrast,
    float MipBrightness, int StartLevel, int Brightness, float Red, float Green, float Blue, bool Alpha);

/// <summary>Documented TexGen copy and twelve-column stitching recipes. Paths stay relative to Data.</summary>
public static class TextureRecipe
{
    public static string AssetPath(string value)
    {
        GamePath.ValidateRelative(value.Trim());
        var p = GamePath.Normalize(value.Trim());
        GamePath.ValidateRelative(p);
        if (!(p.StartsWith("textures\\", StringComparison.Ordinal) || p.StartsWith("dyndolod-temp\\textures\\", StringComparison.Ordinal)) || !p.EndsWith(".dds", StringComparison.Ordinal))
            throw new FormatException("Expected a texture DDS path: " + value);
        return p;
    }

    public static string Sources(string value) => string.Join('|', value.Split('|').Select(AssetPath));

    public static IEnumerable<string> Lines(string text) => text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .Select(s => s.Trim()).Where(s => s.Length > 0 && !s.StartsWith("//") && !s.StartsWith('#'));

    public static TextureCopy ParseCopy(string line)
    {
        var p = line.Split(',', StringSplitOptions.TrimEntries);
        if (p.Length != 2) throw new FormatException("A copy rule needs source,destination.");
        return new(Sources(p[0]), AssetPath(p[1]));
    }

    public static TextureTile ParseTile(string line, bool alpha)
    {
        var p = line.Split('\t', StringSplitOptions.TrimEntries);
        if (p.Length != 12) throw new FormatException("A stitching rule needs twelve tab-separated columns.");
        float F(int i)
        {
            if (!float.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var f) || !float.IsFinite(f))
                throw new FormatException("Invalid number in column " + i);
            return f;
        }
        var r = new TextureTile(Sources(p[4]), AssetPath(p[9]), F(5), F(6), F(7), F(8), F(10), F(11),
            int.Parse(p[0], CultureInfo.InvariantCulture), F(1), F(2), F(3), alpha);
        if (r.Width <= 0 || r.Height <= 0 || r.AtlasWidth <= 0 || r.AtlasHeight <= 0
            || r.Red < 0 || r.Green < 0 || r.Blue < 0 || r.Brightness is < -255 or > 255)
            throw new FormatException("Invalid stitching dimensions or colour settings.");
        return r;
    }

    // Conversion rows retain the source dimensions. The four numeric fields between
    // the x markers describe mip filtering/contrast/brightness/start, not tile placement.
    public static TextureConversion ParseConversion(string line, bool alpha)
    {
        var p = line.Split('\t', StringSplitOptions.TrimEntries);
        if (p.Length != 12 || !p[5].Equals("x", StringComparison.OrdinalIgnoreCase)
            || !p[11].Equals("x", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("A conversion rule needs twelve columns with x in columns 5 and 11.");
        float F(int i) => float.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && float.IsFinite(v)
            ? v : throw new FormatException("Invalid conversion number in column " + i);
        int I(int i) => int.Parse(p[i], NumberStyles.Integer, CultureInfo.InvariantCulture);
        var r = new TextureConversion(Sources(p[4]), AssetPath(p[9]), I(6), F(7), F(8), I(10), I(0), F(1), F(2), F(3), alpha);
        if (r.Filter is < 0 or > 9 || r.StartLevel is < 0 or > 31 || r.Contrast is < -100 or > 100
            || r.MipBrightness is < -100 or > 100 || r.Brightness is < -255 or > 255
            || r.Red < 0 || r.Green < 0 || r.Blue < 0)
            throw new FormatException("Unsupported conversion filter or invalid colour/mipmap settings.");
        return r;
    }

    public static string PluginToken(string name) => new(name.Where(c => char.IsAsciiLetterOrDigit(c) || c == '_').ToArray());
}
