using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PresenceSync.Core.Resources;
using PresenceSync.Core.Presence;
using PresenceSync.Core.Sync;

namespace PresenceSync.App.ViewModels;

/// <summary>Actions the tray UI can trigger. Implemented by <see cref="AppController"/>.</summary>
internal interface ITrayActions
{
    void SetManual(IReadOnlyCollection<string>? tenantIds, ManualStatus status, TimeSpan? duration);
    void ResetManual(IReadOnlyCollection<string>? tenantIds);
    void SetAutoSync(bool enabled);
    Task SignInAsync(string tenantId);
    Task ToggleTenantAsync(string tenantId);
    void OpenSettings();
    void OpenLog();
    void Exit();
}

/// <summary>State of the tray flyout and context menu, updated from every <see cref="SyncSnapshot"/>.</summary>
internal sealed partial class TrayViewModel : ObservableObject
{
    private readonly ITrayActions _actions;
    private bool _applying;

    public TrayViewModel(ITrayActions actions)
    {
        _actions = actions;
        _stateGlyph = Theme.Image(PresenceGlyph.Offline);
    }

    public ObservableCollection<TenantItemViewModel> Tenants { get; } = [];

    public ITrayActions Actions => _actions;

    [ObservableProperty]
    private string _subtitle = Strings.Flyout_Starting;

    [ObservableProperty]
    private ImageSource _stateGlyph;

    [ObservableProperty]
    private bool _autoSync;

    [ObservableProperty]
    private bool _isConfigured;

    [ObservableProperty]
    private bool _hasTenants;

    public SyncSnapshot? Snapshot { get; private set; }

    partial void OnAutoSyncChanged(bool value)
    {
        if (!_applying)
            _actions.SetAutoSync(value);
    }

    public void Apply(SyncSnapshot? snapshot, bool paused)
    {
        _applying = true;
        try
        {
            Snapshot = snapshot;
            IsConfigured = snapshot is not null;
            if (snapshot is null)
            {
                Tenants.Clear();
                HasTenants = false;
                StateGlyph = Theme.Image(PresenceGlyph.Warning);
                Subtitle = Strings.Flyout_NotConfigured;
                return;
            }

            AutoSync = snapshot.AutoSync;
            StateGlyph = Theme.Image(Theme.GlyphFor(snapshot, paused));
            Subtitle = Describe(snapshot, paused);

            for (var i = Tenants.Count - 1; i >= 0; i--)
                if (snapshot.Tenants.All(t => t.TenantId != Tenants[i].TenantId))
                    Tenants.RemoveAt(i);
            for (var i = 0; i < snapshot.Tenants.Count; i++)
            {
                var tenant = snapshot.Tenants[i];
                var item = Tenants.FirstOrDefault(t => t.TenantId == tenant.TenantId);
                if (item is null)
                {
                    item = new TenantItemViewModel(tenant.TenantId, _actions);
                    Tenants.Insert(Math.Min(i, Tenants.Count), item);
                }
                item.Apply(tenant);
            }
            HasTenants = Tenants.Count > 0;
        }
        finally
        {
            _applying = false;
        }
    }

    private static string Describe(SyncSnapshot snapshot, bool paused)
    {
        if (paused)
            return Strings.Flyout_Paused;
        if (snapshot.Tenants.Count == 0)
            return Strings.Flyout_NoTenants;
        var problems = snapshot.Tenants.Count(t => t.HasProblem);
        if (problems > 0)
            return problems == 1 ? Strings.Flyout_OneProblem : Strings.Format(nameof(Strings.Flyout_Problems), problems);
        if (!snapshot.AutoSync)
            return Strings.Flyout_AutoSyncOff;
        var sources = snapshot.Tenants.Where(t => t.Role == TenantRole.Source).Select(t => t.Name).ToList();
        if (sources.Count > 0)
            return Strings.Format(nameof(Strings.Flyout_SyncActive), string.Join(", ", sources));
        return snapshot.TrayState == TrayState.Busy ? Strings.Presence_Busy : Strings.Flyout_AllAvailable;
    }

    public string ToolTip
    {
        get
        {
            if (Snapshot is null)
                return Strings.Tray_NotConfigured;
            var lines = Snapshot.Tenants.Where(t => t.Enabled).Select(t => $"{t.Name}: {t.StatusLine}").Prepend(Strings.Format(nameof(Strings.Tray_TooltipHeader), Subtitle));
            var text = string.Join("\n", lines);
            return text.Length <= 127 ? text : text[..124] + "…";
        }
    }

    [RelayCommand]
    private void QuickDoNotDisturb() => _actions.SetManual(null, ManualStatus.DoNotDisturb, TimeSpan.FromHours(1));

    [RelayCommand]
    private void QuickBusy() => _actions.SetManual(null, ManualStatus.Busy, TimeSpan.FromHours(1));

    [RelayCommand]
    private void QuickAway() => _actions.SetManual(null, ManualStatus.BeRightBack, TimeSpan.FromMinutes(30));

    [RelayCommand]
    private void ResetAll() => _actions.ResetManual(null);

    [RelayCommand]
    private void OpenSettings() => _actions.OpenSettings();

    [RelayCommand]
    private void OpenLog() => _actions.OpenLog();

    [RelayCommand]
    private void Exit() => _actions.Exit();
}

internal sealed partial class TenantItemViewModel(string tenantId, ITrayActions actions) : ObservableObject
{
    public string TenantId { get; } = tenantId;

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private string? _detail;

    [ObservableProperty]
    private ImageSource? _glyph;

    [ObservableProperty]
    private bool _needsSignIn;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isUsable;

    public void Apply(TenantSnapshot tenant)
    {
        Name = tenant.Name;
        Status = tenant.StatusLine;
        Glyph = Theme.Image(Theme.GlyphFor(tenant));
        IsEnabled = tenant.Enabled;
        NeedsSignIn = tenant.Enabled && tenant.Health is TenantHealth.AuthRequired or TenantHealth.Disabled;
        IsUsable = tenant.Enabled && !NeedsSignIn;
        Detail = tenant switch
        {
            { HealthMessage: { } message } => message,
            { Manual: { } manual } => Strings.Format(nameof(Strings.Detail_ManualUntil), FormatUntil(manual.Until)),
            { OwnSession: { } session } => Strings.Format(nameof(Strings.Detail_SessionUntil), session.Until.ToLocalTime().ToString("t")),
            _ => null,
        };
    }

    private static string FormatUntil(DateTimeOffset until)
    {
        var local = until.ToLocalTime();
        return local.Date == DateTime.Today ? local.ToString("t") : local.ToString("g");
    }

    [RelayCommand]
    private Task SignIn() => actions.SignInAsync(TenantId);
}
