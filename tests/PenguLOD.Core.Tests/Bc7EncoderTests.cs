using PenguLOD.Textures.Bc;

namespace PenguLOD.Core.Tests;

public class Bc7EncoderTests
{
    private static byte[] RoundTrip(byte[] rgba)
    {
        Span<byte> block = stackalloc byte[16];
        Bc7Encoder.EncodeBlock(rgba, block);
        var decoded = new byte[64];
        Bc7Decoder.DecodeBlock(block, decoded);
        return decoded;
    }

    private static byte[] Block(Func<int, (byte R, byte G, byte B, byte A)> texel)
    {
        var rgba = new byte[64];
        for (int i = 0; i < 16; i++) { var (r, g, b, a) = texel(i); rgba[i * 4] = r; rgba[i * 4 + 1] = g; rgba[i * 4 + 2] = b; rgba[i * 4 + 3] = a; }
        return rgba;
    }

    private static double MaxColourError(byte[] a, byte[] b)
    {
        double worst = 0;
        for (int i = 0; i < 16; i++) for (int c = 0; c < 3; c++) worst = Math.Max(worst, Math.Abs(a[i * 4 + c] - b[i * 4 + c]));
        return worst;
    }

    [Theory]
    [InlineData(255, 0, 0, 0, 255, 0)]       // red beside green: the same channel sum
    [InlineData(200, 40, 0, 40, 200, 0)]
    [InlineData(0, 0, 255, 255, 0, 0)]
    public void HueEdgesWithEqualChannelSumsKeepBothColours(int r0, int g0, int b0, int r1, int g1, int b1)
    {
        var rgba = Block(i => i % 4 < 2 ? ((byte)r0, (byte)g0, (byte)b0, (byte)255) : ((byte)r1, (byte)g1, (byte)b1, (byte)255));
        var decoded = RoundTrip(rgba);
        // Previously both endpoints collapsed to the mean and the block decoded as one flat colour.
        Assert.True(MaxColourError(rgba, decoded) <= 4, $"max error {MaxColourError(rgba, decoded)}");
    }

    [Fact]
    public void OpaqueBlocksDecodeFullyOpaque()
    {
        var random = new Random(7);
        for (int n = 0; n < 200; n++)
        {
            var rgba = Block(_ => ((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), (byte)255));
            var decoded = RoundTrip(rgba);
            for (int i = 0; i < 16; i++) Assert.Equal(255, decoded[i * 4 + 3]);
        }
        foreach (byte v in new byte[] { 0, 128, 200, 254, 255 })
        {
            var solid = RoundTrip(Block(_ => (v, v, v, (byte)255)));
            for (int i = 0; i < 16; i++) { Assert.Equal(255, solid[i * 4 + 3]); Assert.InRange(solid[i * 4] - v, -1, 1); }
        }
    }

    [Fact]
    public void TranslucentBlocksStillChooseTheBestPBit()
    {
        var rgba = Block(i => ((byte)100, (byte)100, (byte)100, (byte)(i < 8 ? 64 : 128)));
        var decoded = RoundTrip(rgba);
        for (int i = 0; i < 16; i++) Assert.InRange(decoded[i * 4 + 3] - rgba[i * 4 + 3], -3, 3);
    }
}
