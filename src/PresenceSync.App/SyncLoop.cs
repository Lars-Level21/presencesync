using Microsoft.Extensions.Logging;
using PresenceSync.Core.Sync;

namespace PresenceSync.App;

/// <summary>Background polling loop. Can be triggered early (resume, manual change) and paused (no network).</summary>
internal sealed class SyncLoop(SyncEngine engine, Func<TimeSpan> interval, ILogger log) : IAsyncDisposable
{
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly CancellationTokenSource _cts = new();
    private Task? _task;
    private volatile bool _paused;

    public bool IsPaused => _paused;

    public void Start() => _task ??= Task.Run(() => RunAsync(_cts.Token));

    public void TriggerNow()
    {
        if (_wake.CurrentCount == 0)
        {
            try { _wake.Release(); } catch (SemaphoreFullException) { }
        }
    }

    public void Pause()
    {
        if (_paused) return;
        _paused = true;
        log.LogInformation("Sync pausiert (kein Netzwerk)");
    }

    public void Resume()
    {
        if (!_paused) return;
        _paused = false;
        log.LogInformation("Sync fortgesetzt");
        TriggerNow();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (!_paused)
            {
                try
                {
                    await engine.PollAsync(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "Poll fehlgeschlagen");
                }
            }

            try
            {
                await _wake.WaitAsync(interval(), ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_task is not null)
        {
            try { await _task; } catch (OperationCanceledException) { }
        }
        _cts.Dispose();
    }
}
