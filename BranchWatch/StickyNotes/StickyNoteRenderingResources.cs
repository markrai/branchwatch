using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using WpfStyle = System.Windows.Style;
using WpfScrollBar = System.Windows.Controls.Primitives.ScrollBar;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace BranchWatch;

internal static class StickyNoteRenderingResources
{
    private static readonly Lazy<IReadOnlyDictionary<string, SolidColorBrush>> Brushes = new(CreateBrushes);
    private static readonly ConditionalWeakTable<Dispatcher, WpfStyle> ScrollBarStyles = new();

    internal static SolidColorBrush BrushFor(string value) => Brushes.Value[value];

    private static IReadOnlyDictionary<string, SolidColorBrush> CreateBrushes()
    {
        var result = new Dictionary<string, SolidColorBrush>(StringComparer.OrdinalIgnoreCase);
        var values = StickyNoteAppearance.Palette.SelectMany(color => new[] { color.Body, color.Header, color.Alarm })
            .Concat(["#33000000", "#29251F"]);
        foreach (var value in values.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var brush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value)!);
            brush.Freeze();
            result.Add(value, brush);
        }

        return result;
    }

    internal static void ApplyScrollBarStyle(WpfTextBox text)
    {
        text.Dispatcher.VerifyAccess();
        var style = ScrollBarStyles.GetValue(text.Dispatcher, _ =>
        {
            var dictionary = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/BranchWatch;component/StickyNotes/StickyNoteScrollBar.xaml")
            };
            var shared = (WpfStyle)dictionary[typeof(WpfScrollBar)];
            shared.Seal();
            return shared;
        });
        text.Resources[typeof(WpfScrollBar)] = style;
    }
}
