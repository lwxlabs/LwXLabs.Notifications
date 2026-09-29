using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace LwXLabs.Notifications.AspNetCore;

internal sealed class NotificationApplicationQuota
{
    private sealed class Window
    {
        public long Minute;
        public int Count;
    }

    private readonly ConcurrentDictionary<Guid, Window> _sends = new();
    private readonly ConcurrentDictionary<Guid, Window> _registrations = new();
    private readonly NotificationServerOptions _options;

    public NotificationApplicationQuota(IOptions<NotificationServerOptions> options) => _options = options.Value;

    public bool TrySend(Guid applicationId) => TryConsume(_sends, applicationId, _options.MaxSendsPerMinute);
    public bool TryRegister(Guid applicationId) => TryConsume(_registrations, applicationId, _options.MaxRegistrationsPerMinute);

    private static bool TryConsume(ConcurrentDictionary<Guid, Window> windows, Guid applicationId, int limit)
    {
        var window = windows.GetOrAdd(applicationId, static _ => new Window());
        var minute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        lock (window)
        {
            if (window.Minute != minute)
            {
                window.Minute = minute;
                window.Count = 0;
            }
            if (window.Count >= limit) return false;
            window.Count++;
            return true;
        }
    }
}
