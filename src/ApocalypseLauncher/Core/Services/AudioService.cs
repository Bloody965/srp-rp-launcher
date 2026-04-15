using System;
using System.IO;

namespace ApocalypseLauncher.Core.Services;

public class AudioService
{
    private bool _isMusicEnabled = true;

    public void PlayBackgroundMusic(string audioFilePath)
    {
        try
        {
            if (!File.Exists(audioFilePath))
            {
                Console.WriteLine($"Audio file not found: {audioFilePath}");
                return;
            }

            // Note: Audio playback requires additional library like NAudio
            // Install: dotnet add package NAudio
            // For now, this is a placeholder
            Console.WriteLine($"Playing background music: {audioFilePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error playing audio: {ex.Message}");
        }
    }

    public void StopBackgroundMusic()
    {
        Console.WriteLine("Stopping background music");
    }

    public void SetMusicEnabled(bool enabled)
    {
        _isMusicEnabled = enabled;
        if (!enabled)
        {
            StopBackgroundMusic();
        }
    }

    public bool IsMusicEnabled => _isMusicEnabled;
}
