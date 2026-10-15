using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PresenceSync.Core.Presence;
using PresenceSync.Core.Sync;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace PresenceSync.App;

/// <summary>Presence symbols as in Teams: colour plus shape, so Busy and Do not disturb are distinguishable.</summary>
internal enum PresenceGlyph
{
    Available,
    Busy,
    DoNotDisturb,
    Away,
    Offline,
    Warning,
}

/// <summary>Light/dark theme from Windows, presence symbols and the generated tray icons.</summary>
internal static class Theme
{
    public static void ApplySystem() =>
        ApplicationThemeManager.Apply(IsSystemLight() ? ApplicationTheme.Light : ApplicationTheme.Dark, WindowBackdropType.Mica);

    private static bool IsSystemLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
    }

    // ------------------------------------------------------------------ mapping

    public static PresenceGlyph GlyphFor(TenantSnapshot tenant)
    {
        if (!tenant.Enabled)
            return PresenceGlyph.Offline;
        if (tenant.Health is TenantHealth.AuthRequired or TenantHealth.Disabled)
            return PresenceGlyph.Warning;
        return GlyphFor(tenant.Presence);
    }

    public static PresenceGlyph GlyphFor(PresenceInfo? presence) => (presence?.Availability ?? "").ToLowerInvariant() switch
    {
        "available" or "availableidle" => PresenceGlyph.Available,
        "busy" or "busyidle" => PresenceGlyph.Busy,
        "donotdisturb" => PresenceGlyph.DoNotDisturb,
        "away" or "berightback" => PresenceGlyph.Away,
        _ => PresenceGlyph.Offline,
    };

    public static PresenceGlyph GlyphFor(ManualStatus status) => GlyphFor(status.ToPreferredPresence());

    /// <summary>Aggregated symbol for header and tray: DnD wins over Busy if any active tenant is on DnD.</summary>
    public static PresenceGlyph GlyphFor(SyncSnapshot? snapshot, bool paused)
    {
        if (snapshot is null)
            return PresenceGlyph.Warning;
        if (paused)
            return PresenceGlyph.Offline;
        return snapshot.TrayState switch
        {
            TrayState.Warning => PresenceGlyph.Warning,
            TrayState.Off => PresenceGlyph.Offline,
            TrayState.Busy when snapshot.Tenants.Any(t => t.Enabled && GlyphFor(t.Presence) == PresenceGlyph.DoNotDisturb)
                => PresenceGlyph.DoNotDisturb,
            TrayState.Busy => PresenceGlyph.Busy,
            _ => PresenceGlyph.Available,
        };
    }

    // ------------------------------------------------------------------ drawing

    private static readonly Dictionary<PresenceGlyph, DrawingImage> Images = new();

    /// <summary>Vector image of the symbol for menus and windows (scales with DPI).</summary>
    public static ImageSource Image(PresenceGlyph glyph)
    {
        if (!Images.TryGetValue(glyph, out var image))
        {
            image = new DrawingImage(Drawing(glyph, withBackdrop: false));
            image.Freeze();
            Images[glyph] = image;
        }
        return image;
    }

    /// <summary>The symbol in a 16×16 coordinate space.</summary>
    private static Drawing Drawing(PresenceGlyph glyph, bool withBackdrop)
    {
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            var center = new Point(8, 8);
            // Transparent frame keeps the bounds at 16×16 for every symbol.
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 16, 16));
            if (withBackdrop)
                dc.DrawEllipse(Brushes.White, null, center, 8, 8);

            var white = new Pen(Brushes.White, 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            switch (glyph)
            {
                case PresenceGlyph.Available:
                    dc.DrawEllipse(Color("#6BB700"), null, center, 7, 7);
                    dc.DrawGeometry(null, white, Geometry.Parse("M 4.8,8.2 L 7.1,10.4 L 11.3,5.9"));
                    break;
                case PresenceGlyph.Busy:
                    dc.DrawEllipse(Color("#D74654"), null, center, 7, 7);
                    break;
                case PresenceGlyph.DoNotDisturb:
                    dc.DrawEllipse(Color("#D74654"), null, center, 7, 7);
                    dc.DrawRoundedRectangle(Brushes.White, null, new Rect(4.4, 7.1, 7.2, 1.8), 0.9, 0.9);
                    break;
                case PresenceGlyph.Away:
                    dc.DrawEllipse(Color("#F8D22A"), null, center, 7, 7);
                    dc.DrawGeometry(null, white, Geometry.Parse("M 8,4.6 L 8,8.3 L 10.6,9.6"));
                    break;
                case PresenceGlyph.Warning:
                    dc.DrawEllipse(Color("#F7630C"), null, center, 7, 7);
                    dc.DrawGeometry(null, white, Geometry.Parse("M 8,4.4 L 8,8.8"));
                    dc.DrawEllipse(Brushes.White, null, new Point(8, 11.2), 1, 1);
                    break;
                default: // Offline: grey ring with an x
                    var grey = Color("#8A8886");
                    dc.DrawEllipse(withBackdrop ? Brushes.White : Brushes.Transparent, new Pen(grey, 1.6), center, 6.2, 6.2);
                    var cross = new Pen(grey, 1.4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                    dc.DrawLine(cross, new Point(5.9, 5.9), new Point(10.1, 10.1));
                    dc.DrawLine(cross, new Point(10.1, 5.9), new Point(5.9, 10.1));
                    break;
            }
        }
        group.Freeze();
        return group;
    }

    private static SolidColorBrush Color(string hex)
    {
        var brush = new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Tray icon: the symbol on a white disc, rendered at 32 px. Returned as System.Drawing.Icon because H.NotifyIcon
    /// cannot convert a RenderTargetBitmap via IconSource. Creates a new instance on every call on purpose: the tray
    /// icon takes ownership and releases the previous icon, so instances must not be reused.
    /// </summary>
    public static System.Drawing.Icon TrayIcon(PresenceGlyph glyph)
    {
        const int size = 32;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(size / 16.0, size / 16.0));
            dc.DrawDrawing(Drawing(glyph, withBackdrop: true));
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var png = new MemoryStream();
        encoder.Save(png);
        png.Position = 0;
        using var drawingBitmap = new System.Drawing.Bitmap(png);
        var handle = drawingBitmap.GetHicon();
        try
        {
            using var temp = System.Drawing.Icon.FromHandle(handle);
            return (System.Drawing.Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
