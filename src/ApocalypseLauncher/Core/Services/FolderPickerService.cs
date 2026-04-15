using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace ApocalypseLauncher.Core.Services;

public class FolderPickerService
{
    public async Task<string?> PickFolderAsync(Window window, string title = "Выберите папку")
    {
        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            return folders[0].Path.LocalPath;
        }

        return null;
    }

    public string GetDefaultMinecraftDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, ".apocalypse_minecraft");
    }
}
