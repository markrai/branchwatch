using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using WpfBorder = System.Windows.Controls.Border;

namespace BranchWatch;

internal sealed class StickyNoteAlarmPulse(WpfBorder outline)
{
    private static readonly Lazy<DoubleAnimation> Pulse = new(CreatePulse);
    private bool _active;
    private string? _color;

    internal void Update(bool active, string color)
    {
        if (!active)
        {
            if (!_active) return;
            outline.BeginAnimation(UIElement.OpacityProperty, null);
            outline.Visibility = Visibility.Collapsed;
            outline.ClearValue(WpfBorder.BorderBrushProperty);
            outline.ClearValue(UIElement.EffectProperty);
            _active = false;
            _color = null;
            return;
        }

        if (_color != color)
        {
            _color = color;
            var accent = StickyNoteRenderingResources.BrushFor(color);
            outline.BorderBrush = accent;
            outline.Effect = new DropShadowEffect { Color = accent.Color, ShadowDepth = 0, BlurRadius = 10, Opacity = 0.85 };
        }
        if (_active) return;
        _active = true;
        outline.Visibility = Visibility.Visible;
        outline.BeginAnimation(UIElement.OpacityProperty, Pulse.Value);
    }

    private static DoubleAnimation CreatePulse()
    {
        var animation = new DoubleAnimation(0.3, 1.0, TimeSpan.FromSeconds(1.8))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        animation.Freeze();
        return animation;
    }
}
