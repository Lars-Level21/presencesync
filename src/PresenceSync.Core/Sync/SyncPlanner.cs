using PresenceSync.Core.Presence;

namespace PresenceSync.Core.Sync;

/// <summary>Input for one tenant: only tenants whose presence was read successfully in this poll.</summary>
public sealed record TenantView(
    string TenantId,
    PresenceInfo Presence,
    OwnSession? OwnSession,
    bool ManualActive,
    SourceSuppression? Suppression = null);

public abstract record SyncAction(string TenantId);

public sealed record SetSessionAction(string TenantId, SyncKind Kind) : SyncAction(TenantId);

public sealed record ClearSessionAction(string TenantId) : SyncAction(TenantId);

public sealed record SyncPlan(IReadOnlySet<string> Sources, IReadOnlyList<SyncAction> Actions);

/// <summary>
/// Pure decision logic of the auto-sync (spec 8.2). No I/O, no clock of its own, fully unit-testable.
/// </summary>
/// <remarks>
/// Feedback protection: a tenant in which the app holds its own session is never a source.
/// Known limitation (accepted for the use case): if a real call starts in such a tenant while the app's
/// session is active, it is not detected as a new source, because /me/presence only returns the aggregated
/// state and cannot tell the app's Busy/InACall apart from a real one. It becomes a source once the app's
/// session has been cleared and the source cooldown has passed.
/// </remarks>
public static class SyncPlanner
{
    public static SyncPlan Plan(
        IReadOnlyCollection<TenantView> tenants,
        bool autoSync,
        DateTimeOffset now,
        TimeSpan refreshThreshold)
    {
        var actions = new List<SyncAction>();

        if (!autoSync)
        {
            actions.AddRange(tenants.Where(t => t.OwnSession is not null).Select(t => new ClearSessionAction(t.TenantId)));
            return new SyncPlan(new HashSet<string>(), actions);
        }

        var sources = tenants
            .Where(t => t.OwnSession is null
                        && PresenceRules.IsOccupied(t.Presence)
                        && t.Suppression?.Applies(t.Presence, now) != true)
            .ToList();
        var sourceIds = sources.Select(s => s.TenantId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (sources.Count == 0)
        {
            actions.AddRange(tenants.Where(t => t.OwnSession is not null).Select(t => new ClearSessionAction(t.TenantId)));
            return new SyncPlan(sourceIds, actions);
        }

        var kind = sources.Any(s => PresenceRules.KindFor(s.Presence) == SyncKind.DoNotDisturb)
            ? SyncKind.DoNotDisturb
            : SyncKind.Busy;

        foreach (var target in tenants.Where(t => !sourceIds.Contains(t.TenantId)))
        {
            if (target.ManualActive)
            {
                // Manual status has priority: the app sets nothing here and drops a leftover session.
                if (target.OwnSession is not null)
                    actions.Add(new ClearSessionAction(target.TenantId));
                continue;
            }

            var session = target.OwnSession;
            if (session is null || session.Kind != kind || session.Until - now < refreshThreshold)
                actions.Add(new SetSessionAction(target.TenantId, kind));
        }

        return new SyncPlan(sourceIds, actions);
    }
}
