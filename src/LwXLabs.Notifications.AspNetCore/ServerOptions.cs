using System;

namespace LwXLabs.Notifications.AspNetCore;

public sealed class NotificationServerOptions
{
    public string? AdminKey { get; set; }
    public int MaxEnvelopeBytes { get; set; } = 16 * 1024;
    public int MaxComponents { get; set; } = 8;
    public int MaxSendsPerMinute { get; set; } = 600;
    public int MaxRegistrationsPerMinute { get; set; } = 60;
    public TimeSpan DefaultLifetime { get; set; } = TimeSpan.FromHours(24);
    public TimeSpan AckGracePeriod { get; set; } = TimeSpan.FromSeconds(3);
    public TimeSpan CancelledRetention { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan ExpiredRetention { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan FailedRetention { get; set; } = TimeSpan.FromHours(24);
    public TimeSpan FlowSuccessRetention { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan FlowFailureRetention { get; set; } = TimeSpan.FromHours(24);
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(15);
}
