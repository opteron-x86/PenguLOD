using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.Textures;
using AnvilLOD.Textures.Bc;
using AnvilLOD.Textures.Dds;

namespace AnvilLOD.Textures;

/// <summary>CPU conversion of full-size textures with independently filtered, progressively adjusted mip levels.</summary>
public static class TextureConversionRenderer
{
    public static byte[] Encode(TextureImage source, TextureConversion rule, float brightness = 1)
    {
        var blocks = Levels(source, rule, brightness).Select(level => Bc7Encoder.EncodeImage(level.Pixels, level.Width, level.Height)).ToList();
        using var output = new MemoryStream();
        DdsFile.WriteBc7(output, source.Width, source.Height, blocks);
        return output.ToArray();
    }

    public static IEnumerable<TextureImage> Levels(TextureImage source, TextureConversion rule, float brightness = 1)
    {
        if (source.Width is < 1 or > 16384 || source.Height is < 1 or > 16384
            || source.Pixels.Length != checked(source.Width * source.Height * 4))
            throw new InvalidDataException("Conversion texture dimensions are invalid or exceed 16384 pixels.");
        var pixels = source.Pixels.ToArray();
        float[] multipliers = [MathF.Pow(rule.Red, 1 / 2.2f), MathF.Pow(rule.Green, 1 / 2.2f), MathF.Pow(rule.Blue, 1 / 2.2f)];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if ((i & 65535) == 0) RunLimits.Check();
            for (int c = 0; c < 3; c++) pixels[i + c] = Byte((pixels[i + c] * multipliers[c] + rule.Brightness) * brightness);
            if (!rule.Alpha) pixels[i + 3] = 255;
        }
        var original = new TextureImage(pixels, source.Width, source.Height);
        int width = source.Width, height = source.Height;
        for (int mip = 0; ; mip++)
        {
            RunLimits.Check();
            // Every level samples the original; adjusting a mip must never feed the next one.
            var image = mip == 0 ? new TextureImage(pixels.ToArray(), width, height) : Resize(original, width, height, rule.Filter);
            int steps = Math.Max(0, mip - rule.StartLevel + 1);
            if (steps > 0 && (rule.Contrast != 0 || rule.MipBrightness != 0))
                for (int i = 0; i < image.Pixels.Length; i += 4)
                    for (int c = 0; c < 3; c++) image.Pixels[i + c] = Byte(image.Pixels[i + c] * (1 + rule.Contrast * steps / 100) + 255 * rule.MipBrightness * steps / 100);
            if (!rule.Alpha) for (int i = 3; i < image.Pixels.Length; i += 4) image.Pixels[i] = 255;
            yield return image;
            if (width == 1 && height == 1) break;
            width = Math.Max(1, width / 2); height = Math.Max(1, height / 2);
        }
    }

    private readonly record struct Tap(int Index, float Weight);
    private static readonly double[] Radius = [1, 1, 1, 1, 1.5, 1.25, 2, 3, 2, 2];

    public static TextureImage Resize(TextureImage image, int width, int height, int filter)
    {
        if (filter is < 0 or > 9 || width < 1 || height < 1 || width > image.Width || height > image.Height)
            throw new ArgumentOutOfRangeException(nameof(filter));
        var xs = Weights(image.Width, width, filter); var ys = Weights(image.Height, height, filter);
        // Process one destination row at a time; the intermediate never scales with source height.
        var output = new byte[checked(width * height * 4)];
        var row = new float[checked(image.Width * 4)];
        for (int y = 0; y < height; y++)
        {
            RunLimits.Check(); Array.Clear(row);
            foreach (var tap in ys[y])
                for (int x = 0; x < row.Length; x++) row[x] += image.Pixels[tap.Index * row.Length + x] * tap.Weight;
            for (int x = 0; x < width; x++)
                for (int c = 0; c < 4; c++)
                {
                    double value = 0;
                    foreach (var tap in xs[x]) value += row[tap.Index * 4 + c] * tap.Weight;
                    output[(y * width + x) * 4 + c] = Byte(value);
                }
        }
        return new(output, width, height);
    }

    private static Tap[][] Weights(int source, int target, int filter)
    {
        var result = new Tap[target][]; double scale = (double)source / target;
        for (int i = 0; i < target; i++)
        {
            double center = (i + .5) * scale - .5;
            if (source == 1) { result[i] = [new(0, 1)]; continue; }
            if (filter == 0) { result[i] = [new((int)((long)i * (((long)source << 16) / target) >> 16), 1)]; continue; }
            double radius = Radius[filter] * scale;
            var taps = new Dictionary<int, double>();
            for (int j = (int)Math.Floor(center - radius); j <= Math.Ceiling(center + radius); j++)
            {
                double weight = Kernel((j - center) / scale, filter) / scale;
                if (Math.Abs(weight) < 1e-12) continue;
                int at = Math.Clamp(j, 0, source - 1);
                taps[at] = taps.GetValueOrDefault(at) + weight;
            }
            result[i] = taps.Select(t => new Tap(t.Key, (float)t.Value)).ToArray();
        }
        return result;
    }

    private static double Kernel(double x, int filter)
    {
        x = Math.Abs(x);
        return filter switch
        {
            1 => x < 1 ? 1 - x : 0,
            2 => x < 1 ? (1 + Math.Cos(Math.PI * x)) / 2 : 0,
            3 => x < 1 ? (2 * x - 3) * x * x + 1 : 0,
            4 => x < .5 ? .75 - x * x : x < 1.5 ? .5 * Math.Pow(x - 1.5, 2) : 0,
            5 => Math.Exp(-2 * x * x) * Math.Sqrt(2 / Math.PI),
            6 => Cubic(x, 1, 0),
            7 => x < 1e-9 ? 1 : x < 3 ? Math.Sin(Math.PI * x) * Math.Sin(Math.PI * x / 3) / (Math.PI * Math.PI * x * x / 3) : 0,
            8 => Cubic(x, 1d / 3, 1d / 3),
            9 => Cubic(x, 0, .5),
            _ => throw new ArgumentOutOfRangeException(nameof(filter))
        };
    }
    private static double Cubic(double x, double b, double c) => x < 1
        ? ((12 - 9 * b - 6 * c) * x * x * x + (-18 + 12 * b + 6 * c) * x * x + 6 - 2 * b) / 6
        : x < 2 ? ((-b - 6 * c) * x * x * x + (6 * b + 30 * c) * x * x + (-12 * b - 48 * c) * x + 8 * b + 24 * c) / 6 : 0;
    private static byte Byte(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);
}
