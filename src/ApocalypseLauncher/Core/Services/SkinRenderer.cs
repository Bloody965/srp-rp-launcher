using SkiaSharp;
using System;
using System.Collections.Generic;

namespace ApocalypseLauncher.Core.Services;

public class SkinRenderer
{
    public static byte[] RenderSkin3D(string skinPath, bool isSlimModel = false, int width = 256, int height = 512)
    {
        return RenderInteractiveSkin3D(skinPath, isSlimModel, -20f, 12f, width, height);
    }

    public static byte[] RenderInteractiveSkin3D(string skinPath, bool isSlimModel, float yawDegrees, float pitchDegrees, int width, int height)
    {
        using var skinBitmap = SKBitmap.Decode(skinPath);
        if (skinBitmap == null)
            throw new Exception("Failed to load skin");

        var isLegacySkin = IsLegacySkin(skinBitmap);
        var effectiveSlimModel = isSlimModel && !isLegacySkin;

        var info = new SKImageInfo(Math.Max(1, width), Math.Max(1, height));
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        var yaw = MathF.PI / 180f * NormalizeAngle(yawDegrees);
        var pitch = MathF.PI / 180f * Math.Clamp(pitchDegrees, -35f, 35f);
        var scale = MathF.Min(width / 20f, height / 38f);
        var center = new SKPoint(width / 2f, height * 0.82f);

        var textures = BuildTextures(skinBitmap, effectiveSlimModel, isLegacySkin);
        ModelTextures? overlayTextures = null;
        if (!isLegacySkin)
        {
            overlayTextures = BuildOverlayTextures(skinBitmap, effectiveSlimModel);
        }

        try
        {
            var armWidth = effectiveSlimModel ? 3f : 4f;
            var parts = BuildPartsWithOverlays(armWidth, textures, overlayTextures);
            var faces = new List<RenderedFace>(parts.Count * 6);

            foreach (var part in parts)
            {
                faces.Add(CreateRenderedFace(CreateFrontFace(part.Box), part.Textures.Front, 1.00f, yaw, pitch, scale, center, part.IsOverlay));
                faces.Add(CreateRenderedFace(CreateBackFace(part.Box), part.Textures.Back, 0.76f, yaw, pitch, scale, center, part.IsOverlay));
                faces.Add(CreateRenderedFace(CreateRightFace(part.Box), part.Textures.Right, 0.84f, yaw, pitch, scale, center, part.IsOverlay));
                faces.Add(CreateRenderedFace(CreateLeftFace(part.Box), part.Textures.Left, 0.84f, yaw, pitch, scale, center, part.IsOverlay));
                faces.Add(CreateRenderedFace(CreateTopFace(part.Box), part.Textures.Top, 1.08f, yaw, pitch, scale, center, part.IsOverlay));
                faces.Add(CreateRenderedFace(CreateBottomFace(part.Box), part.Textures.Bottom, 0.72f, yaw, pitch, scale, center, part.IsOverlay));
            }

            faces.Sort((a, b) =>
            {
                var d = a.Depth.CompareTo(b.Depth);
                if (d != 0)
                {
                    return d;
                }

                return a.IsOverlay.CompareTo(b.IsOverlay);
            });

            using var paint = new SKPaint
            {
                IsAntialias = false,
                FilterQuality = SKFilterQuality.None,
                Style = SKPaintStyle.Fill
            };

            foreach (var face in faces)
            {
                DrawTexturedFace(canvas, paint, face);
            }
        }
        finally
        {
            DisposeTextures(textures);
            if (overlayTextures != null)
            {
                DisposeTextures(overlayTextures.Value);
            }
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static float NormalizeAngle(float angleDegrees)
    {
        var normalized = angleDegrees % 360f;
        return normalized < 0f ? normalized + 360f : normalized;
    }

    private static bool IsLegacySkin(SKBitmap skinBitmap)
    {
        // Legacy Minecraft skins are 64x32 (or higher-res multiples with half-height).
        // They don't contain separate textures for left arm/leg (and overlay layers).
        return skinBitmap.Height > 0 && skinBitmap.Width > 0 && skinBitmap.Height <= (skinBitmap.Width / 2);
    }

    private const float OuterLayerPad = 0.25f;

    private static Box3 InflateBox(Box3 box, float pad)
    {
        return new Box3(box.X - pad, box.Y - pad, box.Z - pad, box.Width + 2f * pad, box.Height + 2f * pad, box.Depth + 2f * pad);
    }

    private static List<ModelPart> BuildPartsWithOverlays(float armWidth, ModelTextures baseTextures, ModelTextures? overlayTextures)
    {
        var rightLeg = new Box3(-4f, 0f, -2f, 4f, 12f, 4f);
        var leftLeg = new Box3(0f, 0f, -2f, 4f, 12f, 4f);
        var body = new Box3(-4f, 12f, -2f, 8f, 12f, 4f);
        var rightArm = new Box3(-4f - armWidth, 12f, -2f, armWidth, 12f, 4f);
        var leftArm = new Box3(4f, 12f, -2f, armWidth, 12f, 4f);
        var head = new Box3(-4f, 24f, -4f, 8f, 8f, 8f);

        var parts = new List<ModelPart>(overlayTextures != null ? 12 : 6)
        {
            new(rightLeg, baseTextures.RightLeg, false),
            new(leftLeg, baseTextures.LeftLeg, false),
            new(body, baseTextures.Body, false),
            new(rightArm, baseTextures.RightArm, false),
            new(leftArm, baseTextures.LeftArm, false),
            new(head, baseTextures.Head, false)
        };

        if (overlayTextures != null)
        {
            var o = overlayTextures.Value;
            parts.Add(new(InflateBox(rightLeg, OuterLayerPad), o.RightLeg, true));
            parts.Add(new(InflateBox(leftLeg, OuterLayerPad), o.LeftLeg, true));
            parts.Add(new(InflateBox(body, OuterLayerPad), o.Body, true));
            parts.Add(new(InflateBox(rightArm, OuterLayerPad), o.RightArm, true));
            parts.Add(new(InflateBox(leftArm, OuterLayerPad), o.LeftArm, true));
            parts.Add(new(InflateBox(head, OuterLayerPad), o.Head, true));
        }

        return parts;
    }

    /// <summary>Второй слой скина (шляпа, рукава, штаны и т.д.), UV как в Java / skinview3d.</summary>
    private static ModelTextures BuildOverlayTextures(SKBitmap skinBitmap, bool isSlimModel)
    {
        var armFrontWidth = isSlimModel ? 3 : 4;
        var rightArmBackX = isSlimModel ? 51 : 52;
        var rightArmRightX = isSlimModel ? 47 : 48;
        var rightArmBottomX = isSlimModel ? 47 : 48;
        var leftArmBackX = isSlimModel ? 59 : 60;
        var leftArmRightX = isSlimModel ? 55 : 56;
        var leftArmBottomX = isSlimModel ? 55 : 56;

        var head = new FaceTextures(
            ExtractTexture(skinBitmap, 40, 8, 8, 8, 8, 8),
            ExtractTexture(skinBitmap, 56, 8, 8, 8, 8, 8),
            ExtractTexture(skinBitmap, 48, 8, 8, 8, 8, 8),
            ExtractTexture(skinBitmap, 32, 8, 8, 8, 8, 8),
            ExtractTexture(skinBitmap, 40, 0, 8, 8, 8, 8),
            ExtractTexture(skinBitmap, 48, 0, 8, 8, 8, 8));

        var body = new FaceTextures(
            ExtractTexture(skinBitmap, 20, 36, 8, 12, 8, 12),
            ExtractTexture(skinBitmap, 32, 36, 8, 12, 8, 12),
            ExtractTexture(skinBitmap, 28, 36, 4, 12, 4, 12),
            ExtractTexture(skinBitmap, 16, 36, 4, 12, 4, 12),
            ExtractTexture(skinBitmap, 20, 32, 8, 4, 8, 4),
            ExtractTexture(skinBitmap, 28, 32, 8, 4, 8, 4));

        var rightArm = new FaceTextures(
            ExtractTexture(skinBitmap, 44, 36, armFrontWidth, 12, armFrontWidth, 12),
            ExtractTexture(skinBitmap, rightArmBackX, 36, armFrontWidth, 12, armFrontWidth, 12),
            ExtractTexture(skinBitmap, rightArmRightX, 36, 4, 12, 4, 12),
            ExtractTexture(skinBitmap, 40, 36, 4, 12, 4, 12),
            ExtractTexture(skinBitmap, 44, 32, armFrontWidth, 4, armFrontWidth, 4),
            ExtractTexture(skinBitmap, rightArmBottomX, 32, armFrontWidth, 4, armFrontWidth, 4));

        var rightLeg = new FaceTextures(
            ExtractTexture(skinBitmap, 4, 36, 4, 12, 4, 12),
            ExtractTexture(skinBitmap, 12, 36, 4, 12, 4, 12),
            ExtractTexture(skinBitmap, 8, 36, 4, 12, 4, 12),
            ExtractTexture(skinBitmap, 0, 36, 4, 12, 4, 12),
            ExtractTexture(skinBitmap, 4, 32, 4, 4, 4, 4),
            ExtractTexture(skinBitmap, 8, 32, 4, 4, 4, 4));

        var leftArm = new FaceTextures(
            ExtractTexture(skinBitmap, 52, 52, armFrontWidth, 12, armFrontWidth, 12),
            ExtractTexture(skinBitmap, leftArmBackX, 52, armFrontWidth, 12, armFrontWidth, 12),
            ExtractTexture(skinBitmap, leftArmRightX, 52, 4, 12, 4, 12),
            ExtractTexture(skinBitmap, 48, 52, 4, 12, 4, 12),
            ExtractTexture(skinBitmap, 52, 48, armFrontWidth, 4, armFrontWidth, 4),
            ExtractTexture(skinBitmap, leftArmBottomX, 48, armFrontWidth, 4, armFrontWidth, 4));

        var leftLeg = new FaceTextures(
            ExtractTexture(skinBitmap, 4, 52, 4, 12, 4, 12),
            ExtractTexture(skinBitmap, 12, 52, 4, 12, 4, 12),
            ExtractTexture(skinBitmap, 8, 52, 4, 12, 4, 12),
            ExtractTexture(skinBitmap, 0, 52, 4, 12, 4, 12),
            ExtractTexture(skinBitmap, 4, 48, 4, 4, 4, 4),
            ExtractTexture(skinBitmap, 8, 48, 4, 4, 4, 4));

        return new ModelTextures(
            Head: head,
            Body: body,
            RightArm: rightArm,
            LeftArm: leftArm,
            RightLeg: rightLeg,
            LeftLeg: leftLeg);
    }

    private static ModelTextures BuildTextures(SKBitmap skinBitmap, bool isSlimModel, bool isLegacySkin)
    {
        var armFrontWidth = isSlimModel ? 3 : 4;
        var rightArmRightX = isSlimModel ? 47 : 48;
        var rightArmBackX = isSlimModel ? 51 : 52;
        var rightArmBottomX = isSlimModel ? 47 : 48;
        var leftArmRightX = isSlimModel ? 39 : 40;
        var leftArmBackX = isSlimModel ? 43 : 44;
        var leftArmBottomX = isSlimModel ? 39 : 40;

        var head = new FaceTextures(
            ExtractTexture(skinBitmap, 8, 8, 8, 8, 8, 8),
            ExtractTexture(skinBitmap, 24, 8, 8, 8, 8, 8),
            ExtractTexture(skinBitmap, 16, 8, 8, 8, 8, 8),
            ExtractTexture(skinBitmap, 0, 8, 8, 8, 8, 8),
            ExtractTexture(skinBitmap, 8, 0, 8, 8, 8, 8),
            ExtractTexture(skinBitmap, 16, 0, 8, 8, 8, 8));

        var body = new FaceTextures(
            ExtractTexture(skinBitmap, 20, 20, 8, 12, 8, 12),
            ExtractTexture(skinBitmap, 32, 20, 8, 12, 8, 12),
            ExtractTexture(skinBitmap, 28, 20, 4, 12, 4, 12),
            ExtractTexture(skinBitmap, 16, 20, 4, 12, 4, 12),
            ExtractTexture(skinBitmap, 20, 16, 8, 4, 8, 4),
            ExtractTexture(skinBitmap, 28, 16, 8, 4, 8, 4));

        var rightArm = new FaceTextures(
            ExtractTexture(skinBitmap, 44, 20, armFrontWidth, 12, armFrontWidth, 12),
            ExtractTexture(skinBitmap, rightArmBackX, 20, armFrontWidth, 12, armFrontWidth, 12),
            ExtractTexture(skinBitmap, rightArmRightX, 20, 4, 12, 4, 12),
            ExtractTexture(skinBitmap, 40, 20, 4, 12, 4, 12),
            ExtractTexture(skinBitmap, 44, 16, armFrontWidth, 4, armFrontWidth, 4),
            ExtractTexture(skinBitmap, rightArmBottomX, 16, armFrontWidth, 4, armFrontWidth, 4));

        var rightLeg = new FaceTextures(
            ExtractTexture(skinBitmap, 4, 20, 4, 12, 4, 12),
            ExtractTexture(skinBitmap, 12, 20, 4, 12, 4, 12),
            ExtractTexture(skinBitmap, 8, 20, 4, 12, 4, 12),
            ExtractTexture(skinBitmap, 0, 20, 4, 12, 4, 12),
            ExtractTexture(skinBitmap, 4, 16, 4, 4, 4, 4),
            ExtractTexture(skinBitmap, 8, 16, 4, 4, 4, 4));

        FaceTextures leftArm;
        FaceTextures leftLeg;

        if (isLegacySkin)
        {
            // 64x32 skins don't have dedicated left arm/leg textures.
            // To avoid "half a model unskinned", reuse right limb textures.
            // Swapping left/right faces gives a closer-looking result than direct copy.
            leftArm = CloneWithSwappedSides(rightArm);
            leftLeg = CloneWithSwappedSides(rightLeg);
        }
        else
        {
            leftArm = new FaceTextures(
                ExtractTexture(skinBitmap, 36, 52, armFrontWidth, 12, armFrontWidth, 12),
                ExtractTexture(skinBitmap, leftArmBackX, 52, armFrontWidth, 12, armFrontWidth, 12),
                ExtractTexture(skinBitmap, leftArmRightX, 52, 4, 12, 4, 12),
                ExtractTexture(skinBitmap, 32, 52, 4, 12, 4, 12),
                ExtractTexture(skinBitmap, 36, 48, armFrontWidth, 4, armFrontWidth, 4),
                ExtractTexture(skinBitmap, leftArmBottomX, 48, armFrontWidth, 4, armFrontWidth, 4));

            leftLeg = new FaceTextures(
                ExtractTexture(skinBitmap, 20, 52, 4, 12, 4, 12),
                ExtractTexture(skinBitmap, 28, 52, 4, 12, 4, 12),
                ExtractTexture(skinBitmap, 24, 52, 4, 12, 4, 12),
                ExtractTexture(skinBitmap, 16, 52, 4, 12, 4, 12),
                ExtractTexture(skinBitmap, 20, 48, 4, 4, 4, 4),
                ExtractTexture(skinBitmap, 24, 48, 4, 4, 4, 4));
        }

        return new ModelTextures(
            Head: head,
            Body: body,
            RightArm: rightArm,
            LeftArm: leftArm,
            RightLeg: rightLeg,
            LeftLeg: leftLeg);
    }

    private static FaceTextures CloneWithSwappedSides(FaceTextures source)
    {
        return new FaceTextures(
            Front: CloneBitmap(source.Front),
            Back: CloneBitmap(source.Back),
            Right: CloneBitmap(source.Left),
            Left: CloneBitmap(source.Right),
            Top: CloneBitmap(source.Top),
            Bottom: CloneBitmap(source.Bottom));
    }

    private static SKBitmap CloneBitmap(SKBitmap source)
    {
        var clone = new SKBitmap(source.Width, source.Height, source.ColorType, source.AlphaType);
        using var canvas = new SKCanvas(clone);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(source, 0, 0);
        return clone;
    }

    private static void DisposeTextures(ModelTextures textures)
    {
        DisposeFace(textures.Head);
        DisposeFace(textures.Body);
        DisposeFace(textures.RightArm);
        DisposeFace(textures.LeftArm);
        DisposeFace(textures.RightLeg);
        DisposeFace(textures.LeftLeg);
    }

    private static void DisposeFace(FaceTextures face)
    {
        face.Front.Dispose();
        face.Back.Dispose();
        face.Right.Dispose();
        face.Left.Dispose();
        face.Top.Dispose();
        face.Bottom.Dispose();
    }

    private static RenderedFace CreateRenderedFace(Face3 face, SKBitmap texture, float brightness, float yaw, float pitch, float scale, SKPoint center, bool isOverlay)
    {
        var points = new SKPoint[4];
        float depth = 0f;

        for (var i = 0; i < face.Corners.Length; i++)
        {
            var rotated = Rotate(face.Corners[i], yaw, pitch);
            points[i] = new SKPoint(center.X + rotated.X * scale, center.Y - rotated.Y * scale);
            depth += rotated.Z;
        }

        return new RenderedFace(points, texture, depth / face.Corners.Length, brightness, isOverlay);
    }

    private static Vector3 Rotate(Vector3 point, float yaw, float pitch)
    {
        var cosYaw = MathF.Cos(yaw);
        var sinYaw = MathF.Sin(yaw);
        var x1 = point.X * cosYaw + point.Z * sinYaw;
        var z1 = -point.X * sinYaw + point.Z * cosYaw;

        var cosPitch = MathF.Cos(pitch);
        var sinPitch = MathF.Sin(pitch);
        var y2 = point.Y * cosPitch - z1 * sinPitch;
        var z2 = point.Y * sinPitch + z1 * cosPitch;

        return new Vector3(x1, y2, z2);
    }

    private static void DrawTexturedFace(SKCanvas canvas, SKPaint paint, RenderedFace face)
    {
        var texture = face.Texture;
        var width = Math.Max(1, texture.Width);
        var height = Math.Max(1, texture.Height);

        for (var y = 0; y < height; y++)
        {
            var v0 = y / (float)height;
            var v1 = (y + 1) / (float)height;

            for (var x = 0; x < width; x++)
            {
                var color = texture.GetPixel(x, y);
                if (color.Alpha == 0)
                {
                    continue;
                }

                var u0 = x / (float)width;
                var u1 = (x + 1) / (float)width;

                var p0 = Bilinear(face.Points[0], face.Points[1], face.Points[3], face.Points[2], u0, v0);
                var p1 = Bilinear(face.Points[0], face.Points[1], face.Points[3], face.Points[2], u1, v0);
                var p2 = Bilinear(face.Points[0], face.Points[1], face.Points[3], face.Points[2], u1, v1);
                var p3 = Bilinear(face.Points[0], face.Points[1], face.Points[3], face.Points[2], u0, v1);

                using var path = new SKPath();
                path.MoveTo(p0);
                path.LineTo(p1);
                path.LineTo(p2);
                path.LineTo(p3);
                path.Close();

                paint.Color = ApplyBrightness(color, face.Brightness);
                canvas.DrawPath(path, paint);
            }
        }
    }

    private static SKPoint Bilinear(SKPoint topLeft, SKPoint topRight, SKPoint bottomLeft, SKPoint bottomRight, float u, float v)
    {
        var top = new SKPoint(
            topLeft.X + (topRight.X - topLeft.X) * u,
            topLeft.Y + (topRight.Y - topLeft.Y) * u);

        var bottom = new SKPoint(
            bottomLeft.X + (bottomRight.X - bottomLeft.X) * u,
            bottomLeft.Y + (bottomRight.Y - bottomLeft.Y) * u);

        return new SKPoint(
            top.X + (bottom.X - top.X) * v,
            top.Y + (bottom.Y - top.Y) * v);
    }

    private static SKColor ApplyBrightness(SKColor color, float brightness)
    {
        byte Scale(byte value) => (byte)Math.Clamp((int)Math.Round(value * brightness), 0, 255);
        return new SKColor(Scale(color.Red), Scale(color.Green), Scale(color.Blue), color.Alpha);
    }

    private static SKBitmap ExtractTexture(SKBitmap source, int x, int y, int width, int height, int targetWidth, int targetHeight)
    {
        var textureScale = source.Width / 64f;
        var scaledX = (int)Math.Round(x * textureScale);
        var scaledY = (int)Math.Round(y * textureScale);
        var scaledWidth = Math.Max(1, (int)Math.Round(width * textureScale));
        var scaledHeight = Math.Max(1, (int)Math.Round(height * textureScale));

        var texture = new SKBitmap(Math.Max(1, targetWidth), Math.Max(1, targetHeight));
        texture.Erase(SKColors.Transparent);

        using var canvas = new SKCanvas(texture);
        using var paint = new SKPaint
        {
            IsAntialias = false,
            FilterQuality = SKFilterQuality.None
        };

        var srcRect = new SKRect(scaledX, scaledY, scaledX + scaledWidth, scaledY + scaledHeight);
        var destRect = new SKRect(0, 0, texture.Width, texture.Height);
        canvas.DrawBitmap(source, srcRect, destRect, paint);
        return texture;
    }

    private static Face3 CreateFrontFace(Box3 box) => new(new[]
    {
        new Vector3(box.MinX, box.MaxY, box.MaxZ),
        new Vector3(box.MaxX, box.MaxY, box.MaxZ),
        new Vector3(box.MaxX, box.MinY, box.MaxZ),
        new Vector3(box.MinX, box.MinY, box.MaxZ)
    });

    private static Face3 CreateBackFace(Box3 box) => new(new[]
    {
        new Vector3(box.MaxX, box.MaxY, box.MinZ),
        new Vector3(box.MinX, box.MaxY, box.MinZ),
        new Vector3(box.MinX, box.MinY, box.MinZ),
        new Vector3(box.MaxX, box.MinY, box.MinZ)
    });

    private static Face3 CreateRightFace(Box3 box) => new(new[]
    {
        new Vector3(box.MaxX, box.MaxY, box.MaxZ),
        new Vector3(box.MaxX, box.MaxY, box.MinZ),
        new Vector3(box.MaxX, box.MinY, box.MinZ),
        new Vector3(box.MaxX, box.MinY, box.MaxZ)
    });

    private static Face3 CreateLeftFace(Box3 box) => new(new[]
    {
        new Vector3(box.MinX, box.MaxY, box.MinZ),
        new Vector3(box.MinX, box.MaxY, box.MaxZ),
        new Vector3(box.MinX, box.MinY, box.MaxZ),
        new Vector3(box.MinX, box.MinY, box.MinZ)
    });

    private static Face3 CreateTopFace(Box3 box) => new(new[]
    {
        new Vector3(box.MinX, box.MaxY, box.MinZ),
        new Vector3(box.MaxX, box.MaxY, box.MinZ),
        new Vector3(box.MaxX, box.MaxY, box.MaxZ),
        new Vector3(box.MinX, box.MaxY, box.MaxZ)
    });

    private static Face3 CreateBottomFace(Box3 box) => new(new[]
    {
        new Vector3(box.MinX, box.MinY, box.MaxZ),
        new Vector3(box.MaxX, box.MinY, box.MaxZ),
        new Vector3(box.MaxX, box.MinY, box.MinZ),
        new Vector3(box.MinX, box.MinY, box.MinZ)
    });

    private readonly record struct Vector3(float X, float Y, float Z);

    private readonly record struct Box3(float X, float Y, float Z, float Width, float Height, float Depth)
    {
        public float MinX => X;
        public float MaxX => X + Width;
        public float MinY => Y;
        public float MaxY => Y + Height;
        public float MinZ => Z;
        public float MaxZ => Z + Depth;
    }

    private readonly record struct Face3(Vector3[] Corners);
    private readonly record struct FaceTextures(SKBitmap Front, SKBitmap Back, SKBitmap Right, SKBitmap Left, SKBitmap Top, SKBitmap Bottom);
    private readonly record struct ModelTextures(FaceTextures Head, FaceTextures Body, FaceTextures RightArm, FaceTextures LeftArm, FaceTextures RightLeg, FaceTextures LeftLeg);
    private readonly record struct ModelPart(Box3 Box, FaceTextures Textures, bool IsOverlay);
    private readonly record struct RenderedFace(SKPoint[] Points, SKBitmap Texture, float Depth, float Brightness, bool IsOverlay);
}
