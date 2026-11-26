using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using WpfBorder = System.Windows.Controls.Border;

namespace BranchWatch;

internal sealed class StickyNoteAlarmPulse(WpfBorder outline)
{
    private bool _active;
    private string? _color;

    internal void Update(bool active, string color)
    {
        if (!active)
        {
            if (_active) outline.BeginAnimation(UIElement.OpacityProperty, null);
            _active = false;
            outline.Visibility = Visibility.Collapsed;
            return;
        }
        if (_color != color)
        {
            _color = color;
            var accent = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color)!;
            outline.BorderBrush = new SolidColorBrush(accent);
            outline.Effect = new DropShadowEffect { Color = accent, ShadowDepth = 0, BlurRadius = 10, Opacity = 0.85 };
        }
        if (_active) return;
        _active = true;
        outline.Visibility = Visibility.Visible;
        outline.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.3, 1.0, TimeSpan.FromSeconds(1.8))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        });
    }
}
