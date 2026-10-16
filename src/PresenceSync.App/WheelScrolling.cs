using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PresenceSync.App;

/// <summary>
/// Mouse-wheel scrolling like the rest of Windows. WPF's <see cref="ScrollViewer"/> scrolls a full
/// "three lines" step for every wheel message, regardless of its delta. High-resolution wheels (e.g. Logitech
/// smooth scrolling) send many small deltas per notch, so WPF scrolls several times too far. This behavior scrolls
/// proportionally to the delta (120 = one notch) and honours the Windows setting "lines per notch".
/// </summary>
internal static class WheelScrolling
{
    /// <summary>Height of one WPF scroll line in device-independent pixels (same as <see cref="ScrollViewer.LineDown"/>).</summary>
    private const double LineHeight = 16;

    public static readonly DependencyProperty ProportionalProperty = DependencyProperty.RegisterAttached(
        "Proportional", typeof(bool), typeof(WheelScrolling), new PropertyMetadata(false, OnProportionalChanged));

    public static bool GetProportional(DependencyObject element) => (bool)element.GetValue(ProportionalProperty);

    public static void SetProportional(DependencyObject element, bool value) => element.SetValue(ProportionalProperty, value);

    private static void OnProportionalChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer viewer)
            return;
        viewer.PreviewMouseWheel -= OnPreviewMouseWheel;
        if ((bool)e.NewValue)
            viewer.PreviewMouseWheel += OnPreviewMouseWheel;
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var viewer = (ScrollViewer)sender;
        if (viewer.ScrollableHeight <= 0)
            return;

        var notches = e.Delta / (double)Mouse.MouseWheelDeltaForOneLine;
        var lines = SystemParameters.WheelScrollLines;
        var offset = lines < 0
            ? notches * viewer.ViewportHeight // Windows setting "one screen at a time"
            : notches * lines * LineHeight;

        viewer.ScrollToVerticalOffset(viewer.VerticalOffset - offset);
        // Handled at the scroll viewer: controls inside (e.g. number boxes) don't change their value while scrolling.
        e.Handled = true;
    }
}
