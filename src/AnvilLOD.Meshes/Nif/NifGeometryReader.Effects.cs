using System.Numerics;
using AnvilLOD.Core.World;

namespace AnvilLOD.Meshes.Nif;

public static partial class NifGeometryReader
{
    private sealed class UnsupportedEffectException(string message) : NotSupportedException(message);

    private static LodMaterial ReadStaticEffect(NifFile nif, NifBlock block, int alphaRef)
    {
        var r = nif.ReaderFor(block);
        r.I32(); int extras = checked((int)r.U32()); r.Skip(checked(extras * 4));
        if (r.I32() >= 0) throw new UnsupportedEffectException("Animated effect shader controllers are not supported for static rendering.");
        uint f1 = r.U32(), f2 = r.U32();
        // Only static texture/tint, vertex color/alpha, depth and lighting flags are representable.
        const uint allowed1 = 0x80000000u | 2u | 8u | 0x00400000u;
        const uint allowed2 = 1u | 8u | 0x10u | 0x20u | 0x40000000u;
        if ((f1 & ~allowed1) != 0 || (f2 & ~allowed2) != 0)
            throw new UnsupportedEffectException($"Effect shader flags 0x{f1:X8}/0x{f2:X8} require unsupported palette, falloff, refraction or other view-dependent effects.");
        float ox = r.F32(), oy = r.F32(), sx = r.F32(), sy = r.F32();
        string texture = GamePath.NormalizeTexture(r.SizedString());
        uint clamp = r.U8(); float lighting = r.U8() / 255f; r.U8(); r.U8();
        r.Skip(16); // falloff parameters are unused when Use_Falloff is clear
        var tint = new Vector4(r.F32(), r.F32(), r.F32(), r.F32());
        float multiplier = r.F32(); r.F32();
        string palette = r.SizedString();
        if (palette.Length != 0) throw new UnsupportedEffectException("Effect palette textures are not supported for static rendering.");
        if (string.IsNullOrWhiteSpace(texture)) throw new NotSupportedException("Effect shader has no source texture.");
        if (!float.IsFinite(ox + oy + sx + sy + tint.LengthSquared() + multiplier) || multiplier < 0
            || tint.X < 0 || tint.Y < 0 || tint.Z < 0 || tint.W is < 0 or > 1 || clamp > 3)
            throw new InvalidDataException("Effect shader has invalid UV, tint, opacity or clamp values.");
        bool alpha = alphaRef >= 0;
        ushort flags = 0; byte threshold = 0;
        if (alpha)
        {
            if (alphaRef >= nif.Blocks.Count || nif.Blocks[alphaRef].Type != "NiAlphaProperty") throw new InvalidDataException("Invalid effect alpha property.");
            var ar = nif.ReaderFor(nif.Blocks[alphaRef]); ar.I32(); int count = checked((int)ar.U32()); ar.Skip(checked(count * 4)); ar.I32();
            flags = ar.U16(); threshold = ar.U8();
            if ((flags & 1) != 0) throw new UnsupportedEffectException("Blended effect shaders require depth-sorted compositing and are not supported for static rendering.");
        }
        return new([texture], f1, f2, clamp, alpha, flags, threshold, Vector3.Zero, 1,
            new(0, ox, oy, sx, sy, 1, 0, 0, Vector3.Zero, 0, 0, 0),
            new(new(tint.X * multiplier, tint.Y * multiplier, tint.Z * multiplier, tint.W), (f2 & 0x40000000u) != 0 ? lighting : 0));
    }
}
