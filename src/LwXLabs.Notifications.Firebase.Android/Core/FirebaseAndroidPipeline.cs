using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LwXLabs.Notifications.Firebase.Android;

internal sealed record PendingFirebaseEvent(long Sequence, FirebaseAndroidRegistrationChange? Registration, FirebaseAndroidMessage? Message);

internal interface IFirebaseAndroidEventStore
{
    void RecordInstallationId(string installationId, bool confirmCurrent = false);
    void RecordMessage(FirebaseAndroidMessage message);
    PendingFirebaseEvent? Peek();
    void Remove(long sequence);
}

internal interface IFirebaseAndroidJournalStorage
{
    FirebaseAndroidJournalState Read();
    void Write(FirebaseAndroidJournalState state);
}

internal sealed class FirebaseAndroidJournalState
{
    public string? LastInstallationId { get; set; }
    public long NextSequence { get; set; }
    public List<PendingFirebaseEvent> Pending { get; set; } = new();
}

internal interface IFirebaseAndroidPlatform
{
    Task InitializeAsync(CancellationToken cancellationToken);
    bool IsAutoRegistrationEnabled { get; }
    Task RegisterAsync(CancellationToken cancellationToken);
    Task<string> GetInstallationIdAsync(CancellationToken cancellationToken);
}

internal static class FirebaseAndroidMessageNormalizer
{
    public static FirebaseAndroidMessage Normalize(
        string? messageId, string? sender, string? collapseKey, long sentTimeMilliseconds,
        int timeToLiveSeconds, string? title, string? body, IEnumerable<KeyValuePair<string, string>>? data)
    {
        var copy = new Dictionary<string, string>(StringComparer.Ordinal);
        if (data != null)
            foreach (var pair in data)
                if (!string.IsNullOrEmpty(pair.Key)) copy[pair.Key] = pair.Value ?? string.Empty;

        return new FirebaseAndroidMessage(messageId, sender, collapseKey, sentTimeMilliseconds,
            timeToLiveSeconds, title, body, copy);
    }
}

internal sealed class FirebaseAndroidPipeline
{
    private readonly IFirebaseAndroidPlatform _platform;
    private readonly IFirebaseAndroidEventStore _store;
    private readonly Func<IEnumerable<IFirebaseAndroidNotificationHandler>> _handlers;
    private readonly Action<Exception> _reportError;
    private readonly SemaphoreSlim _initialization = new(1, 1);
    private readonly SemaphoreSlim _dispatch = new(1, 1);
    private bool _initialized;

    public FirebaseAndroidPipeline(IFirebaseAndroidPlatform platform, IFirebaseAndroidEventStore store,
        Func<IEnumerable<IFirebaseAndroidNotificationHandler>> handlers, Action<Exception> reportError)
    {
        _platform = platform;
        _store = store;
        _handlers = handlers;
        _reportError = reportError;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _initialization.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_initialized)
            {
                await _platform.InitializeAsync(cancellationToken).ConfigureAwait(false);
                if (!_platform.IsAutoRegistrationEnabled)
                    await _platform.RegisterAsync(cancellationToken).ConfigureAwait(false);
                var installationId = await _platform.GetInstallationIdAsync(cancellationToken).ConfigureAwait(false);
                _store.RecordInstallationId(installationId, confirmCurrent: true);
                _initialized = true;
            }
        }
        finally { _initialization.Release(); }

        // Replay events saved by Firebase before app DI was ready, or after a failed callback.
        await DrainAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> GetInstallationIdAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        var installationId = await _platform.GetInstallationIdAsync(cancellationToken).ConfigureAwait(false);
        _store.RecordInstallationId(installationId);
        await DrainAsync(cancellationToken).ConfigureAwait(false);
        return installationId;
    }

    public async Task DrainAsync(CancellationToken cancellationToken = default)
    {
        await _dispatch.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var handlers = _handlers().ToArray();
            while (_store.Peek() is { } item)
            {
                if (handlers.Length == 0) return;
                try
                {
                    foreach (var handler in handlers)
                    {
                        if (item.Registration != null) await handler.OnRegistrationAsync(item.Registration, cancellationToken).ConfigureAwait(false);
                        else if (item.Message != null) await handler.OnMessageAsync(item.Message, cancellationToken).ConfigureAwait(false);
                    }
                    _store.Remove(item.Sequence);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    try { _reportError(ex); } catch { /* Error reporting cannot break Firebase processing. */ }
                    return; // Keep the event for the next initialization or incoming event.
                }
            }
        }
        finally { _dispatch.Release(); }
    }
}
