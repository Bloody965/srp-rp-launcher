using Avalonia.Controls;
using ApocalypseLauncher.ViewModels;

namespace ApocalypseLauncher.Views;

public partial class MainWindow : Window
{
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
}
