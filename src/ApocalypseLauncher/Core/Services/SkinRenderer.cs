using SkiaSharp;
using System;
using System.IO;

namespace ApocalypseLauncher.Core.Services;

public class SkinRenderer
{
    public static byte[] RenderSkin3D(string skinPath, int size = 256)
    {
        using var skinBitmap = SKBitmap.Decode(skinPath);
        if (skinBitmap == null)
            throw new Exception("Failed to load skin");

        // Определяем масштаб скина (64, 128 или 256)
        int skinSize = skinBitmap.Width;
        float textureScale = skinSize / 64f; // Масштаб относительно стандартного 64x64

        // Создаем canvas для рендера с высоким разрешением
        var info = new SKImageInfo(512, 1024);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        // Масштаб для большего размера
        float scale = 16f;

        // Центрируем без поворота - простой 2D вид спереди
        canvas.Translate(256, 128);

        // Отключаем сглаживание для четких пикселей
        var paint = new SKPaint
        {
            IsAntialias = false,
            FilterQuality = SKFilterQuality.None
        };

        // Рисуем голову (координаты умножаем на textureScale для HD скинов)
        var headFront = ExtractTexture(skinBitmap, (int)(8 * textureScale), (int)(8 * textureScale), (int)(8 * textureScale), (int)(8 * textureScale));
        canvas.DrawBitmap(headFront, new SKRect(-4 * scale, 0, 4 * scale, 8 * scale), paint);

        // Рисуем тело
        var bodyFront = ExtractTexture(skinBitmap, (int)(20 * textureScale), (int)(20 * textureScale), (int)(8 * textureScale), (int)(12 * textureScale));
        canvas.DrawBitmap(bodyFront, new SKRect(-4 * scale, 8 * scale, 4 * scale, 20 * scale), paint);

        // Рисуем правую руку
        var rightArmFront = ExtractTexture(skinBitmap, (int)(44 * textureScale), (int)(20 * textureScale), (int)(4 * textureScale), (int)(12 * textureScale));
        canvas.DrawBitmap(rightArmFront, new SKRect(-8 * scale, 8 * scale, -4 * scale, 20 * scale), paint);

        // Рисуем левую руку
        var leftArmFront = ExtractTexture(skinBitmap, (int)(36 * textureScale), (int)(52 * textureScale), (int)(4 * textureScale), (int)(12 * textureScale));
        canvas.DrawBitmap(leftArmFront, new SKRect(4 * scale, 8 * scale, 8 * scale, 20 * scale), paint);

        // Рисуем правую ногу
        var rightLegFront = ExtractTexture(skinBitmap, (int)(4 * textureScale), (int)(20 * textureScale), (int)(4 * textureScale), (int)(12 * textureScale));
        canvas.DrawBitmap(rightLegFront, new SKRect(-4 * scale, 20 * scale, 0, 32 * scale), paint);

        // Рисуем левую ногу
        var leftLegFront = ExtractTexture(skinBitmap, (int)(20 * textureScale), (int)(52 * textureScale), (int)(4 * textureScale), (int)(12 * textureScale));
        canvas.DrawBitmap(leftLegFront, new SKRect(0, 20 * scale, 4 * scale, 32 * scale), paint);

        // Сохраняем в PNG
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static SKBitmap ExtractTexture(SKBitmap source, int x, int y, int width, int height)
    {
        var texture = new SKBitmap(width, height);
        using var canvas = new SKCanvas(texture);
        var srcRect = new SKRect(x, y, x + width, y + height);
        var destRect = new SKRect(0, 0, width, height);
        canvas.DrawBitmap(source, srcRect, destRect);
        return texture;
    }
}
