using PresenceSync.Core.Resources;

namespace PresenceSync.Core.Configuration;

/// <summary>
/// Values an organisation sets centrally (registry under Software\Policies\PresenceSync, via Intune or Group Policy).
/// Policies win over config.json; the user cannot change policy-controlled values in the app.
/// </summary>
public sealed record PolicySettings(string? ClientId, string? HomeTenantId, bool? AllowCustomClientId)
{
    public static PolicySettings None { get; } = new(null, null, null);

    public bool IsEmpty => string.IsNullOrWhiteSpace(ClientId) && string.IsNullOrWhiteSpace(HomeTenantId) && AllowCustomClientId is null;

    /// <summary>Per value: machine policy (HKLM) wins over user policy (HKCU), like Windows Group Policy.</summary>
    public static PolicySettings Merge(PolicySettings machine, PolicySettings user) => new(
        Pick(machine.ClientId, user.ClientId),
        Pick(machine.HomeTenantId, user.HomeTenantId),
        machine.AllowCustomClientId ?? user.AllowCustomClientId);

    private static string? Pick(string? machine, string? user) =>
        !string.IsNullOrWhiteSpace(machine) ? machine.Trim() : string.IsNullOrWhiteSpace(user) ? null : user.Trim();
}

/// <summary>What the policy controls in the effective configuration (for locking the UI).</summary>
public sealed record PolicyState(bool ClientIdLocked, string? ManagedTenantId, IReadOnlyList<string> Warnings)
{
    public static PolicyState None { get; } = new(false, null, []);

    public bool IsActive => ClientIdLocked || ManagedTenantId is not null;

    public bool IsManagedTenant(string tenantId) =>
        ManagedTenantId is not null && string.Equals(ManagedTenantId, tenantId, StringComparison.OrdinalIgnoreCase);
}

public static class PolicyApplier
{

    /// <summary>
    /// Applies the policy to <paramref name="config"/> in place:
    /// <list type="bullet">
    /// <item>ClientId: enforced and locked, unless AllowCustomClientId = 1 (then only a default for an empty value).</item>
    /// <item>HomeTenantId (GUID): added as home tenant if missing; it cannot be removed in the app.</item>
    /// </list>
    /// Invalid policy values are ignored and reported in <see cref="PolicyState.Warnings"/>.
    /// </summary>
    public static PolicyState Apply(AppConfig config, PolicySettings policy)
    {
        var warnings = new List<string>();
        var locked = false;
        string? managedTenant = null;

        if (policy.ClientId is { } clientId)
        {
            if (!Guid.TryParse(clientId, out _))
            {
                warnings.Add($"Policy ClientId is not a GUID and is ignored: \"{clientId}\"");
            }
            else if (policy.AllowCustomClientId == true)
            {
                if (string.IsNullOrWhiteSpace(config.ClientId))
                    config.ClientId = clientId;
            }
            else
            {
                config.ClientId = clientId;
                locked = true;
            }
        }

        if (policy.HomeTenantId is { } homeTenant)
        {
            if (!Guid.TryParse(homeTenant, out _))
            {
                warnings.Add($"Policy HomeTenantId is not a tenant ID (GUID) and is ignored: \"{homeTenant}\"");
            }
            else
            {
                managedTenant = homeTenant;
                var existing = config.Tenants.FirstOrDefault(t => string.Equals(t.TenantId, homeTenant, StringComparison.OrdinalIgnoreCase));
                if (existing is null)
                {
                    config.Tenants.Insert(0, new TenantConfig
                    {
                        Name = Strings.Policy_HomeTenantDefaultName,
                        TenantId = homeTenant,
                        Upn = "",
                        Enabled = true,
                        IsHome = true,
                    });
                }
                else
                {
                    existing.IsHome = true;
                }
            }
        }

        return new PolicyState(locked, managedTenant, warnings);
    }
}
