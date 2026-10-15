using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using PresenceSync.App.ViewModels;
using PresenceSync.Core.Presence;
using PresenceSync.Core.Sync;
using Wpf.Ui.Controls;
using MenuItem = System.Windows.Controls.MenuItem;

namespace PresenceSync.App;

/// <summary>Builds the status menus used by the tray context menu and the flyout. Styled by WPF-UI.</summary>
internal static class MenuFactory
{
    private static readonly (string Label, Func<TimeSpan?> Duration)[] Durations =
    [
        ("30 Minuten", () => TimeSpan.FromMinutes(30)),
        ("1 Stunde", () => TimeSpan.FromHours(1)),
        ("2 Stunden", () => TimeSpan.FromHours(2)),
        ("Bis Tagesende", UntilEndOfDay),
        ("Unbegrenzt", () => null),
    ];

    /// <summary>Right-click menu of the tray icon.</summary>
    public static void FillTrayMenu(ContextMenu menu, TrayViewModel vm, Action openFlyout)
    {
        menu.Items.Clear();
        menu.Items.Add(Item("PresenceSync öffnen", SymbolRegular.Open24, openFlyout));
        menu.Items.Add(new Separator());

        if (vm.Snapshot is { } snapshot)
        {
            foreach (var item in StatusItems(vm.Actions, null, "Alle: "))
                menu.Items.Add(item);
            menu.Items.Add(new Separator());

            foreach (var tenant in snapshot.Tenants)
                menu.Items.Add(TenantMenu(vm.Actions, tenant));
            if (snapshot.Tenants.Count > 0)
                menu.Items.Add(new Separator());

            var autoSync = new MenuItem { Header = "Auto-Sync", IsCheckable = true, IsChecked = snapshot.AutoSync };
            autoSync.Click += (_, _) => vm.Actions.SetAutoSync(autoSync.IsChecked);
            menu.Items.Add(autoSync);
        }

        menu.Items.Add(Item("Einstellungen …", SymbolRegular.Settings24, vm.Actions.OpenSettings));
        menu.Items.Add(Item("Log öffnen", SymbolRegular.DocumentText24, vm.Actions.OpenLog));
        menu.Items.Add(Item("Beenden", SymbolRegular.Power24, vm.Actions.Exit));
    }

    /// <summary>Per-tenant menu: header with status, info lines, status options, sign-in, enable toggle.</summary>
    public static MenuItem TenantMenu(ITrayActions actions, TenantSnapshot tenant)
    {
        var menu = new MenuItem { Header = $"{tenant.Name}: {tenant.StatusLine}", Icon = GlyphIcon(Theme.GlyphFor(tenant)) };
        FillTenantItems(menu.Items, actions, tenant);
        return menu;
    }

    public static void FillTenantItems(ItemCollection items, ITrayActions actions, TenantSnapshot tenant)
    {
        items.Clear();
        items.Add(Info($"Status: {PresenceText.Describe(tenant.Presence)}"));
        if (tenant.Manual is { } manual)
            items.Add(Info($"Manuell: {PresenceText.Describe(manual.Status)} bis {manual.Until.ToLocalTime():g}"));
        if (tenant.OwnSession is { } session)
            items.Add(Info($"Sync-Session bis {session.Until.ToLocalTime():t}"));
        if (tenant.HealthMessage is { } message)
            items.Add(Info($"⚠ {message}"));
        items.Add(new Separator());

        var blocked = tenant.Health is TenantHealth.AuthRequired or TenantHealth.Disabled;
        if (tenant.Enabled && !blocked)
        {
            foreach (var item in StatusItems(actions, [tenant.TenantId], ""))
                items.Add(item);
            items.Add(new Separator());
        }

        if (tenant.Enabled)
            items.Add(Item(blocked ? "Anmelden …" : "Erneut anmelden …", SymbolRegular.Person24,
                () => _ = actions.SignInAsync(tenant.TenantId)));
        var enabled = new MenuItem { Header = "Aktiviert", IsCheckable = true, IsChecked = tenant.Enabled };
        enabled.Click += (_, _) => _ = actions.ToggleTenantAsync(tenant.TenantId);
        items.Add(enabled);
    }

    /// <summary>One entry per manual status (each with a duration submenu) plus "Zurücksetzen".</summary>
    public static IEnumerable<Control> StatusItems(ITrayActions actions, IReadOnlyCollection<string>? tenantIds, string prefix)
    {
        foreach (var status in Enum.GetValues<ManualStatus>())
        {
            var item = new MenuItem
            {
                Header = prefix + PresenceText.Describe(status),
                Icon = GlyphIcon(Theme.GlyphFor(status)),
            };
            foreach (var (label, duration) in Durations)
                item.Items.Add(Item(label, null, () => actions.SetManual(tenantIds, status, duration())));
            yield return item;
        }
        yield return Item(prefix + "Zurücksetzen", SymbolRegular.ArrowCounterclockwise24, () => actions.ResetManual(tenantIds));
    }

    public static MenuItem Item(string header, SymbolRegular? symbol, Action onClick)
    {
        var item = new MenuItem { Header = header };
        if (symbol is { } s)
            item.Icon = new SymbolIcon { Symbol = s };
        item.Click += (_, _) => onClick();
        return item;
    }

    private static MenuItem Info(string text) => new() { Header = text, IsEnabled = false };

    private static FrameworkElement GlyphIcon(PresenceGlyph glyph) =>
        new System.Windows.Controls.Image { Source = Theme.Image(glyph), Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center };

    private static TimeSpan? UntilEndOfDay()
    {
        var now = DateTime.Now;
        var remaining = now.Date.AddDays(1) - now;
        return remaining < TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : remaining;
    }
}
