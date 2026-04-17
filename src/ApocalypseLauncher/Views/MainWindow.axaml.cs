using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using ApocalypseLauncher.ViewModels;

namespace ApocalypseLauncher.Views;

public partial class MainWindow : Window
{
    private const string SiteUrl = "https://srp-rp.negrn2345.workers.dev/";
    private const string DiscordUrl = "https://discord.gg/VfBYwSZJW5";

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void ChooseFolderButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.ChooseFolderFromWindowAsync(this);
        }
    }

    private void OpenSiteButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        OpenExternalLink(SiteUrl);
    }

    private void OpenDiscordButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        OpenExternalLink(DiscordUrl);
    }

    private static void OpenExternalLink(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private void MinimizeButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void ToggleMaximizeButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginMoveDrag(e);
    }
}
