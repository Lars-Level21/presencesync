using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PresenceSync.Core.Resources;
using PresenceSync.Core.Configuration;
using PresenceSync.Core.Sync;
using PresenceSync.Graph;
using Wpf.Ui.Controls;

namespace PresenceSync.App.ViewModels;

/// <summary>Settings dialog. Works on a copy of the configuration; <see cref="Saved"/> delivers the result.</summary>
internal sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppConfig _config;
    private readonly string _configPath;
    private readonly Func<string, string?, bool, TenantAuthenticator> _authFactory;

    public SettingsViewModel(AppConfig config, string configPath, PolicyState policy, Func<string, string?, bool, TenantAuthenticator> authFactory)
    {
        _config = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(config))!;
        _configPath = configPath;
        _authFactory = authFactory;
        IsClientIdLocked = policy.ClientIdLocked;
        IsPolicyActive = policy.IsActive;

        _clientId = _config.ClientId;
        _pollIntervalSeconds = Math.Clamp(_config.PollIntervalSeconds, 10, 300);
        _sessionExpirationMinutes = Math.Clamp(_config.SessionExpirationMinutes, 5, 240);
        _autoSync = _config.AutoSync;
        _autostart = _config.Autostart;
        _useBroker = _config.UseBroker;
        _hotkeysEnabled = _config.Hotkeys.Enabled;
        _hotkeyDoNotDisturb = _config.Hotkeys.DoNotDisturbOneHour;
        _hotkeyReset = _config.Hotkeys.Reset;
        foreach (var t in _config.Tenants)
            Tenants.Add(new TenantRowViewModel(this, t.Name, t.TenantId, t.Upn, t.Enabled, t.IsHome) { IsManaged = policy.IsManagedTenant(t.TenantId) });
    }

    public event EventHandler<AppConfig>? Saved;

    public event EventHandler? CloseRequested;

    /// <summary>Yes/no question to the user (title, text) → true = confirmed; set by the view.</summary>
    public Func<string, string, Task<bool>> ConfirmAsync { get; set; } = (_, _) => Task.FromResult(false);

    /// <summary>Guest if the account's home tenant differs from the tenant the token was issued for.</summary>
    private static string DescribeAccount(Microsoft.Identity.Client.AuthenticationResult result) =>
        result.Account.HomeAccountId?.TenantId is { } home
        && !string.Equals(home, result.TenantId, StringComparison.OrdinalIgnoreCase)
            ? Strings.Account_Guest
            : Strings.Account_Member;

    /// <summary>Parent window handle for the WAM dialog; set by the view.</summary>
    public Func<IntPtr> OwnerHandle { get; set; } = () => IntPtr.Zero;

    public ObservableCollection<TenantRowViewModel> Tenants { get; } = [];

    /// <summary>Client-ID comes from an organisation policy and cannot be changed here.</summary>
    public bool IsClientIdLocked { get; }

    public bool IsClientIdEditable => !IsClientIdLocked;

    public bool IsPolicyActive { get; }

    /// <summary>
    /// Hidden while a policy is active: editing the file cannot override the policy (re-applied on every start and
    /// save), but offering it next to "managed by your organisation" would be misleading.
    /// </summary>
    public bool CanOpenConfigFile => !IsPolicyActive;

    [ObservableProperty] private string _clientId;
    [ObservableProperty] private double? _pollIntervalSeconds;
    [ObservableProperty] private double? _sessionExpirationMinutes;
    [ObservableProperty] private bool _autoSync;
    [ObservableProperty] private bool _autostart;
    [ObservableProperty] private bool _useBroker;
    [ObservableProperty] private bool _hotkeysEnabled;
    [ObservableProperty] private string _hotkeyDoNotDisturb;
    [ObservableProperty] private string _hotkeyReset;

    [ObservableProperty] private string _newTenant = "";
    [ObservableProperty] private string _newTenantName = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTenantCommand))]
    private bool _isBusy;

    [ObservableProperty] private bool _hasMessage;
    [ObservableProperty] private string _messageTitle = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private InfoBarSeverity _messageSeverity;
    [ObservableProperty] private bool _hasPendingChanges;

    public bool HasTenants => Tenants.Count > 0;

    private bool CanAddTenant() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanAddTenant))]
    private async Task AddTenantAsync()
    {
        var clientId = ClientId.Trim();
        if (!Guid.TryParse(clientId, out _))
        {
            Show(InfoBarSeverity.Warning, Strings.Settings_ClientIdMissingTitle,
                Strings.Settings_ClientIdMissingText);
            return;
        }
        // Empty tenant = Windows account picker ("organizations"); a tenant is only needed for guest accounts.
        var tenant = string.IsNullOrWhiteSpace(NewTenant) ? null : NewTenant.Trim();
        if (tenant is not null && !TenantInput.IsValid(tenant))
        {
            Show(InfoBarSeverity.Warning, Strings.Settings_TenantInvalidTitle, Strings.Settings_TenantInvalidText);
            return;
        }

        IsBusy = true;
        HasMessage = false;
        try
        {
            var auth = _authFactory(clientId, tenant, UseBroker);
            var result = await auth.SignInInteractiveAsync(OwnerHandle(), CancellationToken.None);
            var upn = result.Account.Username;

            var existing = Tenants.FirstOrDefault(t => string.Equals(t.TenantId, result.TenantId, StringComparison.OrdinalIgnoreCase));
            var account = Strings.Format(nameof(Strings.Account_WithType), upn, DescribeAccount(result));
            if (existing is not null)
            {
                if (string.IsNullOrEmpty(existing.Upn))
                {
                    // e.g. home tenant pre-created by policy: first sign-in, nothing to replace.
                    existing.Upn = upn;
                    HasPendingChanges = true;
                    Show(InfoBarSeverity.Success, Strings.Settings_SignedInTitle, Strings.Format(nameof(Strings.Settings_SignedInExisting), existing.Name, account));
                    return;
                }

                if (string.Equals(existing.Upn, upn, StringComparison.OrdinalIgnoreCase))
                {
                    Show(InfoBarSeverity.Informational, Strings.Settings_AlreadySetUpTitle,
                        Strings.Format(nameof(Strings.Settings_AlreadySetUpText), existing.Name, account));
                    return;
                }

                // One account per tenant: never replace the existing account silently.
                var replace = await ConfirmAsync(
                    Strings.Replace_Title,
                    Strings.Format(nameof(Strings.Replace_Text), existing.Name, existing.Upn, account));
                if (!replace)
                {
                    Show(InfoBarSeverity.Informational, Strings.Settings_NotChangedTitle, Strings.Format(nameof(Strings.Settings_NotChangedText), existing.Name, existing.Upn));
                    return;
                }

                var previous = existing.Upn;
                existing.Upn = upn;
                HasPendingChanges = true;
                Show(InfoBarSeverity.Warning, Strings.Settings_ReplacedTitle,
                    Strings.Format(nameof(Strings.Settings_ReplacedText), existing.Name, previous, account));
                return;
            }
            else
            {
                var name = !string.IsNullOrWhiteSpace(NewTenantName) ? NewTenantName.Trim()
                    : await OrganizationLookup.GetDisplayNameAsync(result.AccessToken, CancellationToken.None)
                      ?? upn[(upn.IndexOf('@') + 1)..];
                Tenants.Add(new TenantRowViewModel(this, name, result.TenantId, upn, enabled: true, isHome: Tenants.Count == 0));
                OnPropertyChanged(nameof(HasTenants));
                Show(InfoBarSeverity.Success, Strings.Settings_SignedInTitle, Strings.Format(nameof(Strings.Settings_AddedText), name, account));
            }

            NewTenant = "";
            NewTenantName = "";
            HasPendingChanges = true;
        }
        catch (TenantAccessException ex)
        {
            Show(InfoBarSeverity.Error, Strings.Notify_SignInFailed, ex.UserMessage);
        }
        catch (Exception ex)
        {
            Show(InfoBarSeverity.Error, Strings.Notify_SignInFailed, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    internal void Remove(TenantRowViewModel row)
    {
        if (row.IsManaged)
            return;
        Tenants.Remove(row);
        HasPendingChanges = true;
        OnPropertyChanged(nameof(HasTenants));
    }

    internal void MarkChanged() => HasPendingChanges = true;

    [RelayCommand]
    private void Save()
    {
        _config.ClientId = ClientId.Trim();
        _config.PollIntervalSeconds = (int)Math.Clamp(PollIntervalSeconds ?? 20, 10, 300);
        _config.SessionExpirationMinutes = (int)Math.Clamp(SessionExpirationMinutes ?? 5, 5, 240);
        _config.AutoSync = AutoSync;
        _config.Autostart = Autostart;
        _config.UseBroker = UseBroker;
        _config.Hotkeys.Enabled = HotkeysEnabled;
        _config.Hotkeys.DoNotDisturbOneHour = HotkeyDoNotDisturb.Trim();
        _config.Hotkeys.Reset = HotkeyReset.Trim();
        _config.Tenants = Tenants.Select(t => new TenantConfig
        {
            Name = t.Name.Trim(),
            TenantId = t.TenantId,
            Upn = t.Upn,
            Enabled = t.Enabled,
            IsHome = t.IsHome,
        }).ToList();

        Saved?.Invoke(this, _config);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenConfigFile()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_configPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Show(InfoBarSeverity.Error, "config.json", ex.Message);
        }
    }

    private void Show(InfoBarSeverity severity, string title, string message)
    {
        MessageSeverity = severity;
        MessageTitle = title;
        Message = message;
        HasMessage = true;
    }
}

internal sealed partial class TenantRowViewModel : ObservableObject
{
    private readonly SettingsViewModel _owner;

    public TenantRowViewModel(SettingsViewModel owner, string name, string tenantId, string upn, bool enabled, bool isHome)
    {
        _owner = owner;
        _name = name;
        TenantId = tenantId;
        _upn = upn;
        _enabled = enabled;
        _isHome = isHome;
    }

    public string TenantId { get; }

    /// <summary>Home tenant set by organisation policy: cannot be removed.</summary>
    public bool IsManaged { get; init; }

    public bool CanRemove => !IsManaged;

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _upn;
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private bool _isHome;

    partial void OnNameChanged(string value) => _owner.MarkChanged();
    partial void OnEnabledChanged(bool value) => _owner.MarkChanged();
    partial void OnIsHomeChanged(bool value) => _owner.MarkChanged();

    [RelayCommand]
    private void Remove() => _owner.Remove(this);
}
