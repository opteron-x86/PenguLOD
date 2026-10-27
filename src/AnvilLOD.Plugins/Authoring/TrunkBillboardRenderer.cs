using System.Numerics;
using AnvilLOD.Meshes;

namespace AnvilLOD.Plugins.Authoring;

/// <summary>Recover a trunk image's camera and crop from the installed hybrid's texture coordinates.
/// TexGen crops the two views independently. Reframing to the source NIF's bounds stretches the trunk
/// on an already-built hybrid and separates its branches from the crown.</summary>
internal sealed record TrunkView(Vector3 Right, Vector3 Up, Vector3 Depth,
    float Left, float Bottom, float Width, float Height)
{
    public static TrunkView FromCard(LodMeshPart card, int view)
    {
        Vector2 Uv(int i)
        {
            var uv = card.UVs[i];
            if (card.Material.Passthru is { } shader)
                uv = uv * new Vector2(shader.UvScaleX, shader.UvScaleY) + new Vector2(shader.UvOffsetX, shader.UvOffsetY);
            return uv;
        }
        for (int t = 0; t < card.Triangles.Length; t += 3)
        {
            int a = card.Triangles[t], b = card.Triangles[t + 1], c = card.Triangles[t + 2];
            var uv = Uv(a); var du = Uv(b) - uv; var dv = Uv(c) - uv;
            float determinant = du.X * dv.Y - du.Y * dv.X;
            if (Math.Abs(determinant) < 1e-6f) continue;
            var ab = card.Positions[b] - card.Positions[a]; var ac = card.Positions[c] - card.Positions[a];
            var across = (ab * dv.Y - ac * du.Y) / determinant;
            var down = (ac * du.X - ab * dv.X) / determinant;
            float width = across.Length(), height = down.Length();
            var right = across / width; var up = -down / height;
            var origin = card.Positions[a] - across * uv.X - down * uv.Y;
            if (!float.IsFinite(width + height + origin.LengthSquared()) || width <= 1 || height <= 1
                || Math.Abs(Vector3.Dot(right, up)) > .001f
                || Vector3.Dot(up, Vector3.UnitZ) < .999f
                || Math.Abs(Vector3.Dot(right, view == 1 ? Vector3.UnitX : Vector3.UnitY)) < .999f)
                throw new NotSupportedException("Trunk cards must have rectangular UVs, upright Z and front X / side Y axes.");
            float tolerance = Math.Max(.02f, Math.Max(width, height) * .0001f);
            for (int i = 0; i < card.VertexCount; i++)
            {
                var p = Uv(i);
                if (!float.IsFinite(p.X + p.Y) || p.X < -.001f || p.Y < -.001f || p.X > 1.001f || p.Y > 1.001f
                    || Vector3.Distance(card.Positions[i], origin + across * p.X + down * p.Y) > tolerance)
                    throw new NotSupportedException("Trunk cards have non-planar geometry or inconsistent texture coordinates.");
            }
            // View 2 looks from -X with +Y across the image. Swapping X/Y is intentional:
            // a 90-degree rotation would reverse either the view direction or its texture.
            return new(right, up, view == 1 ? Vector3.UnitY : Vector3.UnitX,
                Vector3.Dot(origin, right), Vector3.Dot(origin, up) - height, width, height);
        }
        throw new InvalidDataException("Trunk card has no usable texture coordinates.");
    }

    public bool SameProjection(TrunkView other)
    {
        float tolerance = Math.Max(.02f, Math.Max(Width, Height) * .0001f);
        return Vector3.Distance(Right, other.Right) < .001f && Vector3.Distance(Up, other.Up) < .001f
            && Math.Abs(Left - other.Left) < tolerance && Math.Abs(Bottom - other.Bottom) < tolerance
            && Math.Abs(Width - other.Width) < tolerance && Math.Abs(Height - other.Height) < tolerance;
    }

    public BillboardImage Render(LodMesh source, BillboardRenderer.TextureLoader loader, int maxSize,
        float unitsPerPixel, float brightness)
    {
        Vector3 Project(Vector3 p) => new(Vector3.Dot(p, Right), Vector3.Dot(p, Depth), Vector3.Dot(p, Up));
        var projected = new LodMesh { Path = source.Path, Parts = source.Parts.Select(p => new LodMeshPart
        {
            Name = p.Name, Material = p.Material, Positions = p.Positions.Select(Project).ToArray(),
            Normals = p.Normals.Select(Project).ToArray(), Tangents = p.Tangents.Select(Project).ToArray(),
            Bitangents = p.Bitangents.Select(Project).ToArray(), UVs = p.UVs, Colors = p.Colors, Triangles = p.Triangles
        }).ToArray() };
        return BillboardRenderer.Render(projected, loader, maxSize, unitsPerPixel: unitsPerPixel,
            viewport: (new(Left, 0, Bottom), new(Left + Width, 0, Bottom + Height)), brightness: brightness)
            ?? throw new InvalidDataException("The trunk rendered no visible pixels inside the hybrid card's crop.");
    }
}
