using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DesktopMemoryFixture;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 1 || (args.Length == 1 && (args[0].Length > 64 || args[0].Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))))
            return 2;

        var label = args.Length == 1 ? args[0] : "Default";
        var window = new Window
        {
            Title = $"Desktop Memory Fixture - {label}",
            Width = 500,
            Height = 340,
            MinWidth = 160,
            MinHeight = 100,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = new SolidColorBrush(Color.FromRgb(239, 244, 250))
        };
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = label, FontSize = 30, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock
        {
            Text = "Desktop Memory test window. Move, resize, minimize or close this window as directed by the qualification guide.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 12)
        });
        panel.Children.Add(new TextBlock { Text = $"Process ID: {Environment.ProcessId}" });
        window.Content = panel;
        return new Application().Run(window);
    }
}
