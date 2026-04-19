using System;
using System.IO;
using System.Linq;

namespace ApocalypseLauncher.API.Services;

public class SkinValidationService
{
    private const int MAX_FILE_SIZE = 2 * 1024 * 1024; // 2 MB для HD скинов

    // Поддержка HD скинов: 64x64, 128x128, 256x256
    private static readonly int[] VALID_SKIN_SIZES = { 64, 128, 256 };

    private const int CAPE_WIDTH = 64;
    private const int CAPE_HEIGHT = 32;

    // PNG file signature (magic bytes)
    private static readonly byte[] PNG_SIGNATURE = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    public (bool isValid, string? error) ValidateSkinFile(Stream fileStream, long fileSize)
    {
        return ValidateHDSkinFile(fileStream, fileSize);
    }

    public (bool isValid, string? error) ValidateCapeFile(Stream fileStream, long fileSize)
    {
        return ValidateImageFile(fileStream, fileSize, CAPE_WIDTH, CAPE_HEIGHT, "плаща");
    }

    private (bool isValid, string? error) ValidateHDSkinFile(Stream fileStream, long fileSize)
    {
        // Проверка размера файла
        if (fileSize > MAX_FILE_SIZE)
        {
            return (false, $"Размер файла скина превышает 2 MB");
        }

        if (fileSize < 100)
        {
            return (false, $"Файл скина слишком маленький");
        }

        // Проверка PNG signature
        var signature = new byte[8];
        fileStream.Position = 0;
        var bytesRead = fileStream.Read(signature, 0, 8);

        if (bytesRead != 8 || !signature.SequenceEqual(PNG_SIGNATURE))
        {
            return (false, $"Файл скина должен быть в формате PNG");
        }

        // Чтение размеров изображения из IHDR chunk
        fileStream.Position = 16;

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

        // Проверка что скин квадратный и допустимого размера (64x64, 128x128, 256x256)
        if (width != height || !VALID_SKIN_SIZES.Contains(width))
        {
            return (false, $"Размер скина должен быть 64x64, 128x128 или 256x256 пикселей (получено {width}x{height})");
        }

        // Проверка глубины цвета и типа цвета
        var bitDepth = fileStream.ReadByte();
        var colorType = fileStream.ReadByte();

        if (bitDepth != 8)
        {
            return (false, $"Глубина цвета скина должна быть 8 бит");
        }

        if (colorType > 6 || colorType == 1 || colorType == 5)
        {
            return (false, $"Некорректный тип цвета PNG файла скина");
        }

        return (true, null);
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

        // Color type: принимаем любой валидный PNG тип (0-6, кроме 1 и 5 которые зарезервированы)
        // 0 = Grayscale, 2 = RGB, 3 = Indexed, 4 = Grayscale+Alpha, 6 = RGBA
        if (colorType > 6 || colorType == 1 || colorType == 5)
        {
            return (false, $"Некорректный тип цвета PNG файла {fileType}");
        }

        // Все валидные PNG типы принимаются

        return (true, null);
    }

    public bool IsValidSkinType(string skinType)
    {
        return skinType == "classic" || skinType == "slim";
    }
}
