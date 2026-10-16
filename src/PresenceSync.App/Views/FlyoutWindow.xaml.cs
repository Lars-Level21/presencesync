using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PresenceSync.App.ViewModels;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace PresenceSync.App.Views;

/// <summary>
/// Windows-11-style flyout above the tray icon (like OneDrive/Teams): tenant cards, quick actions, auto-sync switch.
/// Hides when it loses focus.
/// </summary>
internal partial class FlyoutWindow : FluentWindow
{
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(300);

    private readonly TrayViewModel _vm;
    private DateTime _hiddenAt;
    private int _keepOpen;
    private ContextMenu? _openMenu;

    public FlyoutWindow(TrayViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        SourceInitialized += (_, _) => SystemThemeWatcher.Watch(this, WindowBackdropType.Mica, true);
        Deactivated += (_, _) => HideIfIdle();
        SizeChanged += (_, _) => PositionNearTray();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
                _hiddenAt = DateTime.UtcNow;
        };
    }

    /// <summary>Left click on the tray icon. Clicking the icon deactivates the flyout first, hence the guard.</summary>
    public void Toggle()
    {
        if (IsVisible)
            Hide();
        else if (DateTime.UtcNow - _hiddenAt > ReopenGuard)
            ShowNearTray();
    }

    public void ShowNearTray()
    {
        Show();
        UpdateLayout();
        PositionNearTray();
        Activate();
    }

    /// <summary>Keeps the flyout open while e.g. a WAM sign-in dialog has the focus.</summary>
    public IDisposable KeepOpen()
    {
        _keepOpen++;
        return new Release(() => _keepOpen--);
    }

    private void HideIfIdle()
    {
        if (_keepOpen == 0 && _openMenu?.IsOpen != true)
            Hide();
    }

    /// <summary>Bottom-right corner of the work area (or the corner next to a top/left taskbar).</summary>
    private void PositionNearTray()
    {
        const double margin = 12;
        var area = SystemParameters.WorkArea;
        Left = area.Left > 0 ? area.Left + margin : area.Right - ActualWidth - margin;
        Top = area.Top > 0 ? area.Top + margin : area.Bottom - ActualHeight - margin;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Alt+F4 only hides; the window lives as long as the app.
        if (!Dispatcher.HasShutdownStarted && Application.Current is { } app && !app.Dispatcher.HasShutdownStarted)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    private void OnTenantMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TenantItemViewModel item } target
            || _vm.Snapshot?.Tenants.FirstOrDefault(t => t.TenantId == item.TenantId) is not { } tenant)
            return;

        var menu = new ContextMenu();
        MenuFactory.FillTenantItems(menu.Items, _vm.Actions, tenant);
        Open(menu, target);
    }

    private void OnMoreStatusClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        foreach (var item in MenuFactory.StatusItems(_vm.Actions, null, allTenants: false))
            menu.Items.Add(item);
        Open(menu, (FrameworkElement)sender);
    }

    private void Open(ContextMenu menu, FrameworkElement target)
    {
        menu.PlacementTarget = target;
        menu.Placement = PlacementMode.Bottom;
        menu.Closed += (_, _) =>
        {
            _openMenu = null;
            if (!IsActive)
                HideIfIdle();
        };
        _openMenu = menu;
        menu.IsOpen = true;
    }

    private sealed class Release(Action action) : IDisposable
    {
        private Action? _action = action;

        public void Dispose()
        {
            _action?.Invoke();
            _action = null;
        }
    }
}
