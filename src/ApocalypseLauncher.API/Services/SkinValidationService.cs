using System;
using System.IO;
using System.Linq;

namespace ApocalypseLauncher.API.Services;

public class SkinValidationService
{
    private const int MAX_FILE_SIZE = 8 * 1024 * 1024; // 8 MB для HD скинов/плащей
    private const int MIN_SKIN_SIZE = 64;
    private const int MAX_SKIN_SIZE = 1024;
    private const int CAPE_BASE_WIDTH = 64;
    private const int CAPE_BASE_HEIGHT = 32;

    // PNG file signature (magic bytes)
    private static readonly byte[] PNG_SIGNATURE = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    public (bool isValid, string? error) ValidateSkinFile(Stream fileStream, long fileSize)
    {
        return ValidateHDSkinFile(fileStream, fileSize);
    }

    public (bool isValid, string? error) ValidateCapeFile(Stream fileStream, long fileSize)
    {
        return ValidateImageFile(fileStream, fileSize, CAPE_BASE_WIDTH, CAPE_BASE_HEIGHT, "плаща");
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

        // Поддержка HD скинов: квадрат 64..1024, размер кратен 64.
        if (width != height || !IsValidSkinSize(width))
        {
            return (false, $"Размер скина должен быть квадратным и кратным 64 (от 64x64 до 1024x1024). Получено {width}x{height}");
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

        // Для плаща разрешаем HD-масштабы: 64x32, 128x64, 256x128, ... до 1024x512.
        if (!IsValidCapeSize(width, height, expectedWidth, expectedHeight))
        {
            return (false, $"Размер {fileType} должен быть {expectedWidth}x{expectedHeight} или HD-масштаб (до 1024x512). Получено {width}x{height}");
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

    private static bool IsValidSkinSize(int size)
    {
        return size >= MIN_SKIN_SIZE &&
               size <= MAX_SKIN_SIZE &&
               size % 64 == 0;
    }

    private static bool IsValidCapeSize(int width, int height, int expectedWidth, int expectedHeight)
    {
        if (width < expectedWidth || height < expectedHeight)
        {
            return false;
        }

        if (width > 1024 || height > 512)
        {
            return false;
        }

        // Must preserve base aspect/scale exactly.
        return width % expectedWidth == 0 &&
               height % expectedHeight == 0 &&
               width / expectedWidth == height / expectedHeight;
    }
}
