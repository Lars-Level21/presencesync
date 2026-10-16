using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client.Extensions.Msal;
using Microsoft.Win32;
using PresenceSync.App.ViewModels;
using PresenceSync.App.Views;
using PresenceSync.Core.Resources;
using PresenceSync.Core.Configuration;
using PresenceSync.Core.Presence;
using PresenceSync.Core.Sync;
using PresenceSync.Graph;
using Serilog.Extensions.Logging;

namespace PresenceSync.App;

/// <summary>Lifecycle of the tray app: config, engine, loop, tray icon, flyout, settings and system events.</summary>
internal sealed class AppController : ITrayActions, IDisposable
{
    private readonly Application _app;
    private readonly SerilogLoggerFactory _loggerFactory = new(Serilog.Log.Logger);
    private readonly ILogger _log;
    private readonly ConfigStore _store = new(ConfigStore.DefaultPath);
    private readonly HostWindow _host = new();
    private readonly TaskbarIcon _tray;
    private readonly TrayViewModel _vm;
    private readonly FlyoutWindow _flyout;
    private readonly Dictionary<string, TenantAuthenticator> _auth = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TenantHealth> _lastHealth = new(StringComparer.OrdinalIgnoreCase);

    private AppConfig _config;
    private PolicyState _policy = PolicyState.None;
    private bool _policyLogged;
    private MsalCacheHelper? _cache;
    private GraphPresenceGateway? _gateway;
    private string? _gatewayClientId;
    private SyncEngine? _engine;
    private SyncLoop? _loop;
    private bool _authUsesBroker;
    private SettingsWindow? _settings;
    private bool _exiting;
    private PresenceGlyph _trayGlyph = PresenceGlyph.Offline;

    public AppController(Application app)
    {
        _app = app;
        _log = _loggerFactory.CreateLogger<AppController>();
        _config = LoadConfig();
        _vm = new TrayViewModel(this);
        _flyout = new FlyoutWindow(_vm);

        _tray = new TaskbarIcon
        {
            ToolTipText = Strings.Tray_Starting,
            Icon = Theme.TrayIcon(PresenceGlyph.Offline),
            NoLeftClickDelay = true,
            PopupActivation = PopupActivationMode.None,
            MenuActivation = PopupActivationMode.RightClick,
            ContextMenu = new ContextMenu(),
        };
        _tray.TrayLeftMouseUp += (_, _) => _flyout.Toggle();
        MenuFactory.FillTrayMenu(_tray.ContextMenu, _vm, _flyout.ShowNearTray);
    }

    public void Start()
    {
        _tray.ForceCreate(false);
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        _ = InitializeAsync();
    }

    // ---------------------------------------------------------------- startup / configuration

    private AppConfig LoadConfig()
    {
        AppConfig config;
        try
        {
            config = _store.Load();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "config.json could not be read");
            System.Windows.MessageBox.Show(Strings.Format(nameof(Strings.Notify_ConfigInvalid), ex.Message, _store.Path),
                "PresenceSync", MessageBoxButton.OK, MessageBoxImage.Warning);
            config = new AppConfig();
        }

        EnforcePolicy(config);
        _store.Save(config);
        return config;
    }

    /// <summary>Organisation policies (registry) win over config.json; re-applied before every save.</summary>
    private void EnforcePolicy(AppConfig config)
    {
        var policy = RegistryPolicySource.Read();
        _policy = PolicyApplier.Apply(config, policy);
        foreach (var warning in _policy.Warnings)
            _log.LogWarning("Policy: {Warning}", warning);
        if (_policy.IsActive && !_policyLogged)
        {
            _log.LogInformation("Policy active: client ID {Locked}, managed home tenant {Tenant}",
                _policy.ClientIdLocked ? "enforced" : "not enforced", _policy.ManagedTenantId ?? "–");
            _policyLogged = true;
        }
    }

    private async Task InitializeAsync()
    {
        try
        {
            _cache = await TokenCache.CreateAsync();
            await ApplyConfigAsync(_config);
            if (string.IsNullOrWhiteSpace(_config.ClientId) || _config.Tenants.Count == 0)
                OpenSettings();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Initialization failed");
            Notify(Strings.Notify_StartFailed, ex.Message, NotificationIcon.Error);
        }
    }

    private async Task ApplyConfigAsync(AppConfig config)
    {
        _config = config;
        if (_gateway is null || !string.Equals(_gatewayClientId, config.ClientId, StringComparison.OrdinalIgnoreCase))
        {
            await TearDownAsync();
            if (string.IsNullOrWhiteSpace(config.ClientId))
            {
                UpdateUi(null);
                return;
            }

            _gatewayClientId = config.ClientId;
            _gateway = new GraphPresenceGateway(config.ClientId, _loggerFactory.CreateLogger<GraphPresenceGateway>());
            _engine = new SyncEngine(_gateway, SyncOptions.FromConfig(config), TimeProvider.System, _loggerFactory.CreateLogger<SyncEngine>());
            _engine.SnapshotChanged += (_, snapshot) => _app.Dispatcher.BeginInvoke(() => UpdateUi(snapshot));
            _loop = new SyncLoop(_engine, () => _config.PollInterval, _loggerFactory.CreateLogger<SyncLoop>());
        }

        if (_authUsesBroker != config.UseBroker)
        {
            _auth.Clear();
            _authUsesBroker = config.UseBroker;
        }

        foreach (var tenant in config.Tenants)
        {
            if (_auth.TryGetValue(tenant.TenantId, out var existing)
                && string.Equals(existing.Upn ?? "", tenant.Upn, StringComparison.OrdinalIgnoreCase))
                continue;
            var auth = CreateAuthenticator(config.ClientId, tenant.TenantId, tenant.Upn, config.UseBroker);
            // Policy-managed home tenant without account yet: try the Windows account silently (zero-touch rollout).
            auth.AllowOperatingSystemAccount = _policy.IsManagedTenant(tenant.TenantId) && string.IsNullOrEmpty(tenant.Upn);
            _auth[tenant.TenantId] = auth;
            _gateway!.Register(tenant.TenantId, auth);
        }

        await _engine!.ConfigureAsync(
            config.Tenants.Select(t => new TenantDescriptor(t.TenantId, t.Name, t.Enabled)),
            SyncOptions.FromConfig(config), CancellationToken.None);

        foreach (var removed in _auth.Keys.Where(id => config.Tenants.All(t => !Same(t.TenantId, id))).ToList())
        {
            _auth.Remove(removed);
            _gateway!.Unregister(removed);
        }

        try
        {
            Autostart.Apply(config.Autostart);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Autostart could not be set");
        }

        RegisterHotkeys();
        if (!NetworkInterface.GetIsNetworkAvailable())
            _loop!.Pause();
        _loop!.Start();
        _loop.TriggerNow();

        _log.LogInformation("Configuration active: {Count} tenant(s), interval {Interval}, auto-sync {AutoSync}",
            config.Tenants.Count, config.PollInterval, config.AutoSync);
    }

    private TenantAuthenticator CreateAuthenticator(string clientId, string tenant, string? upn, bool useBroker) =>
        new(clientId, tenant, upn, useBroker, _cache, _loggerFactory.CreateLogger<TenantAuthenticator>());

    private async Task TearDownAsync()
    {
        if (_loop is not null)
            await _loop.DisposeAsync();
        if (_engine is not null)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await Task.Run(() => _engine.ClearOwnSessionsAsync(cts.Token));
        }
        _gateway?.Dispose();
        _loop = null;
        _engine = null;
        _gateway = null;
        _gatewayClientId = null;
        _auth.Clear();
    }

    private void RegisterHotkeys()
    {
        _host.UnregisterAllHotkeys();
        if (!_config.Hotkeys.Enabled)
            return;
        _host.RegisterHotkey(_config.Hotkeys.DoNotDisturbOneHour,
            () => SetManual(null, ManualStatus.DoNotDisturb, TimeSpan.FromHours(1)), _log);
        _host.RegisterHotkey(_config.Hotkeys.Reset, () => ResetManual(null), _log);
    }

    private async Task SaveAndApplyAsync(AppConfig config)
    {
        try
        {
            EnforcePolicy(config);
            _store.Save(config);
            await ApplyConfigAsync(config);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Configuration could not be applied");
            Notify(Strings.Notify_SettingsNotApplied, ex.Message, NotificationIcon.Error);
        }
    }

    // ---------------------------------------------------------------- UI

    private void UpdateUi(SyncSnapshot? snapshot)
    {
        if (_exiting)
            return;

        PersistLearnedAccounts();
        var paused = _loop?.IsPaused == true;
        _vm.Apply(_engine is null ? null : snapshot, paused);
        var glyph = Theme.GlyphFor(_engine is null ? null : snapshot, paused);
        if (glyph != _trayGlyph)
        {
            // New icon instance per change: the tray icon releases the previous one, so instances are never reused.
            _tray.Icon = Theme.TrayIcon(glyph);
            _log.LogInformation("Tray state: {State}", glyph);
            _trayGlyph = glyph;
        }
        _tray.ToolTipText = _vm.ToolTip;
        if (_tray.ContextMenu is { IsOpen: false } menu)
            MenuFactory.FillTrayMenu(menu, _vm, _flyout.ShowNearTray);

        if (snapshot is null)
            return;
        foreach (var t in snapshot.Tenants)
        {
            _lastHealth.TryGetValue(t.TenantId, out var previous);
            if (t.HasProblem && previous != t.Health)
                Notify(Strings.Format(t.Health == TenantHealth.AuthRequired ? nameof(Strings.Notify_TenantSignInRequired) : nameof(Strings.Notify_TenantBlocked), t.Name),
                    (t.HealthMessage ?? "") + "\n" + Strings.Notify_FixInWindow, NotificationIcon.Warning);
            _lastHealth[t.TenantId] = t.Health;
        }
    }

    /// <summary>Saves accounts that were signed in silently (Windows account for the policy home tenant).</summary>
    private void PersistLearnedAccounts()
    {
        var changed = false;
        foreach (var tenant in _config.Tenants.Where(t => string.IsNullOrEmpty(t.Upn)))
        {
            if (_auth.TryGetValue(tenant.TenantId, out var auth) && auth.Upn is { } upn)
            {
                tenant.Upn = upn;
                changed = true;
            }
        }
        if (changed)
            _store.Save(_config);
    }

    private void Notify(string title, string message, NotificationIcon icon)
    {
        if (_exiting)
            return;
        try
        {
            _tray.ShowNotification(title, string.IsNullOrWhiteSpace(message) ? " " : message, icon);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Notification could not be shown");
        }
    }

    // ---------------------------------------------------------------- ITrayActions

    public void SetManual(IReadOnlyCollection<string>? tenantIds, ManualStatus status, TimeSpan? duration) =>
        RunEngine(async (engine, ct) => Report(await engine.SetManualAsync(tenantIds, status, duration, ct)));

    public void ResetManual(IReadOnlyCollection<string>? tenantIds) =>
        RunEngine(async (engine, ct) => Report(await engine.ResetManualAsync(tenantIds, ct)));

    public void SetAutoSync(bool enabled)
    {
        if (_config.AutoSync == enabled)
            return;
        _config.AutoSync = enabled;
        _store.Save(_config);
        RunEngine((engine, ct) => engine.SetAutoSyncAsync(enabled, ct));
    }

    public async Task ToggleTenantAsync(string tenantId)
    {
        var tenant = _config.Tenants.FirstOrDefault(t => Same(t.TenantId, tenantId));
        if (tenant is null)
            return;
        tenant.Enabled = !tenant.Enabled;
        await SaveAndApplyAsync(_config);
    }

    public async Task SignInAsync(string tenantId)
    {
        if (_engine is null || !_auth.TryGetValue(tenantId, out var auth))
            return;
        try
        {
            // Explicit user action only: interactive auth is never started automatically.
            using var _ = _flyout.KeepOpen();
            var result = await auth.SignInInteractiveAsync(ParentWindow(), CancellationToken.None);
            var tenant = _config.Tenants.First(t => Same(t.TenantId, tenantId));
            var upn = result.Account.Username;
            if (!string.IsNullOrEmpty(tenant.Upn) && !string.Equals(tenant.Upn, upn, StringComparison.OrdinalIgnoreCase))
            {
                // A different account was picked in the account dialog: one account per tenant, never replace silently.
                var isGuest = result.Account.HomeAccountId?.TenantId is { } home && !Same(home, result.TenantId);
                var box = new Wpf.Ui.Controls.MessageBox
                {
                    Title = Strings.Replace_Title,
                    Content = new TextBlock
                    {
                        Text = Strings.Format(nameof(Strings.Replace_Text), tenant.Name, tenant.Upn,
                            Strings.Format(nameof(Strings.Account_WithType), upn, isGuest ? Strings.Account_Guest : Strings.Account_Member)),
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 460,
                    },
                    PrimaryButtonText = Strings.Replace_Button,
                    PrimaryButtonAppearance = Wpf.Ui.Controls.ControlAppearance.Caution,
                    CloseButtonText = Strings.Common_Cancel,
                };
                if (await box.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary)
                {
                    // Rebuilds the authenticator for the configured account (its UPN no longer matches).
                    await SaveAndApplyAsync(_config);
                    _log.LogInformation("{Tenant}: account change canceled, keeping the current account", tenant.Name);
                    return;
                }
                _log.LogInformation("{Tenant}: account replaced", tenant.Name);
            }
            if (!string.Equals(tenant.Upn, upn, StringComparison.OrdinalIgnoreCase))
            {
                tenant.Upn = upn;
                _store.Save(_config);
            }
            await _engine.MarkSignedInAsync(tenantId, CancellationToken.None);
            _loop?.TriggerNow();
        }
        catch (TenantAccessException ex)
        {
            _log.LogWarning("Sign-in to {Tenant} failed: {Message}", tenantId, ex.Message);
            Notify(Strings.Notify_SignInFailed, ex.UserMessage, NotificationIcon.Error);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Sign-in to {Tenant} failed", tenantId);
            Notify(Strings.Notify_SignInFailed, ex.Message, NotificationIcon.Error);
        }
    }

    public void OpenSettings()
    {
        _flyout.Hide();
        if (_settings is { IsLoaded: true })
        {
            _settings.Activate();
            return;
        }

        var vm = new SettingsViewModel(_config, _store.Path, _policy,
            (clientId, tenant, useBroker) => tenant is null
                ? TenantAuthenticator.ForAccountSelection(clientId, useBroker, _cache, _loggerFactory.CreateLogger<TenantAuthenticator>())
                : CreateAuthenticator(clientId, tenant, null, useBroker));
        _settings = new SettingsWindow(vm);
        vm.Saved += async (_, config) => await SaveAndApplyAsync(config);
        _settings.Closed += (_, _) => _settings = null;
        _settings.Show();
        _settings.Activate();
    }

    public void OpenLog()
    {
        try
        {
            Directory.CreateDirectory(App.LogDirectory);
            var latest = new DirectoryInfo(App.LogDirectory).GetFiles("*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
            Process.Start(new ProcessStartInfo(latest?.FullName ?? App.LogDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Log could not be opened");
        }
    }

    public async void Exit()
    {
        if (_exiting)
            return;
        _exiting = true;
        _flyout.Hide();
        _tray.Visibility = Visibility.Collapsed;
        if (_loop is not null)
            await _loop.DisposeAsync();
        ClearSessionsBlocking(TimeSpan.FromSeconds(5));
        _app.Shutdown();
    }

    private IntPtr ParentWindow()
    {
        if (_settings is { IsVisible: true })
            return new WindowInteropHelper(_settings).Handle;
        if (_flyout.IsVisible)
            return new WindowInteropHelper(_flyout).Handle;
        return _host.Handle;
    }

    private void RunEngine(Func<SyncEngine, CancellationToken, Task> action)
    {
        var engine = _engine;
        if (engine is null)
        {
            Notify(Strings.Notify_NotConfiguredTitle, Strings.Notify_NotConfiguredText, NotificationIcon.Warning);
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                await action(engine, cts.Token);
                _loop?.TriggerNow();
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Action failed");
                _ = _app.Dispatcher.BeginInvoke(() => Notify(Strings.Notify_ActionFailed, ex.Message, NotificationIcon.Error));
            }
        });
    }

    private void Report(IReadOnlyList<TenantResult> results)
    {
        if (results.Count == 0)
        {
            _app.Dispatcher.BeginInvoke(() => Notify(Strings.Notify_NoActiveTenantTitle, Strings.Notify_NoActiveTenantText,
                NotificationIcon.Warning));
            return;
        }

        var failed = results.Where(r => !r.Success).ToList();
        if (failed.Count > 0)
            _app.Dispatcher.BeginInvoke(() => Notify(Strings.Notify_PartialStatusTitle,
                string.Join("\n", failed.Select(f => $"{f.Name}: {f.Error}")), NotificationIcon.Warning));
    }

    // ---------------------------------------------------------------- system events / exit

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            _log.LogInformation("Resumed from standby – polling now");
            _loop?.TriggerNow();
        }
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (e.IsAvailable)
            _loop?.Resume();
        else
            _loop?.Pause();
        _app.Dispatcher.BeginInvoke(() => UpdateUi(_engine?.Snapshot));
    }

    public void OnSessionEnding(string reason)
    {
        _log.LogInformation("Windows {Reason}: ending own sessions", reason);
        ClearSessionsBlocking(TimeSpan.FromSeconds(3));
    }

    private void ClearSessionsBlocking(TimeSpan timeout)
    {
        var engine = _engine;
        if (engine is null)
            return;
        try
        {
            // Task.Run: no Dispatcher SynchronizationContext inside, so blocking here cannot deadlock.
            using var cts = new CancellationTokenSource(timeout);
            Task.Run(() => engine.ClearOwnSessionsAsync(cts.Token)).Wait(timeout + TimeSpan.FromSeconds(1));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Sessions could not be ended");
        }
    }

    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        _tray.Dispose();
        _host.Dispose();
        _gateway?.Dispose();
        _loggerFactory.Dispose();
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
