using System;
using System.IO;
using System.Linq;

namespace ApocalypseLauncher.API.Services;

public class SkinValidationService
{
    private const int MAX_FILE_SIZE = 1024 * 1024; // 1 MB
    private const int SKIN_WIDTH = 64;
    private const int SKIN_HEIGHT = 64;
    private const int CAPE_WIDTH = 64;
    private const int CAPE_HEIGHT = 32;

    // PNG file signature (magic bytes)
    private static readonly byte[] PNG_SIGNATURE = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    public (bool isValid, string? error) ValidateSkinFile(Stream fileStream, long fileSize)
    {
        return ValidateImageFile(fileStream, fileSize, SKIN_WIDTH, SKIN_HEIGHT, "скина");
    }

    public (bool isValid, string? error) ValidateCapeFile(Stream fileStream, long fileSize)
    {
        return ValidateImageFile(fileStream, fileSize, CAPE_WIDTH, CAPE_HEIGHT, "плаща");
    }

    private (bool isValid, string? error) ValidateImageFile(
        Stream fileStream,
        long fileSize,
        int expectedWidth,
        int expectedHeight,
        string fileType)
    {
        // Проверка размера файла
        if (fileSize > MAX_FILE_SIZE)
        {
            return (false, $"Размер файла {fileType} превышает 1 MB");
        }

        if (fileSize < 100)
        {
            return (false, $"Файл {fileType} слишком маленький");
        }

        // Проверка PNG signature
        var signature = new byte[8];
        fileStream.Position = 0;
        var bytesRead = fileStream.Read(signature, 0, 8);

        if (bytesRead != 8 || !signature.SequenceEqual(PNG_SIGNATURE))
        {
            return (false, $"Файл {fileType} должен быть в формате PNG");
        }

        // Чтение размеров изображения из IHDR chunk
        fileStream.Position = 16; // Пропускаем signature (8) + chunk length (4) + chunk type (4)

        var widthBytes = new byte[4];
        var heightBytes = new byte[4];

        fileStream.Read(widthBytes, 0, 4);
        fileStream.Read(heightBytes, 0, 4);

        // PNG использует big-endian
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(widthBytes);
            Array.Reverse(heightBytes);
        }

        var width = BitConverter.ToInt32(widthBytes, 0);
        var height = BitConverter.ToInt32(heightBytes, 0);

        if (width != expectedWidth || height != expectedHeight)
        {
            return (false, $"Размер {fileType} должен быть {expectedWidth}x{expectedHeight} пикселей (получено {width}x{height})");
        }

        // Проверка глубины цвета и типа цвета
        var bitDepth = fileStream.ReadByte();
        var colorType = fileStream.ReadByte();

        // Bit depth должен быть 8
        if (bitDepth != 8)
        {
            return (false, $"Глубина цвета {fileType} должна быть 8 бит");
        }

        // Color type: 2 (RGB) или 6 (RGBA) - оба допустимы
        if (colorType != 2 && colorType != 6)
        {
            return (false, $"Тип цвета {fileType} должен быть RGB или RGBA");
        }

        return (true, null);
    }

    public bool IsValidSkinType(string skinType)
    {
        return skinType == "classic" || skinType == "slim";
    }
}
