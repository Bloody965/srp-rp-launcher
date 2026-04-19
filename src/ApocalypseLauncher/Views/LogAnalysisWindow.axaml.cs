using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ApocalypseLauncher.Views;

public partial class LogAnalysisWindow : Window
{
    public string AnalysisResult { get; set; } = string.Empty;

    public LogAnalysisWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    public LogAnalysisWindow(string analysisResult) : this()
    {
        AnalysisResult = analysisResult;
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
