using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Flow.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LwXLabs.Notifications.AspNetCore;

internal sealed class NotificationCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly NotificationServerOptions _options;
    private readonly ILogger<NotificationCleanupService> _logger;

    public NotificationCleanupService(IServiceScopeFactory scopeFactory, IOptions<NotificationServerOptions> options, ILogger<NotificationCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Cleanup(stoppingToken).ConfigureAwait(false); }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested) { _logger.LogError(ex, "Notification cleanup failed."); }
            await Task.Delay(_options.CleanupInterval, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task Cleanup(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var now = DateTimeOffset.UtcNow;

        var cancelledBefore = now - _options.CancelledRetention;
        var expiredBefore = now - _options.ExpiredRetention;
        var failedBefore = now - _options.FailedRetention;

        var items = await db.Notifications.Where(x =>
                (x.Status == NotificationItemStatus.Completed && x.ExpiresAt < now) ||
                (x.Status == NotificationItemStatus.Cancelled && x.CompletedAt < cancelledBefore) ||
                (x.Status == NotificationItemStatus.Expired && x.CompletedAt < expiredBefore) ||
                (x.Status == NotificationItemStatus.Failed && x.CompletedAt < failedBefore) ||
                (x.ExpiresAt < now && x.Status != NotificationItemStatus.Completed && x.Status != NotificationItemStatus.Cancelled))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        db.Notifications.RemoveRange(items);

        var flowSuccessBefore = now - _options.FlowSuccessRetention;
        var flowFailureBefore = now - _options.FlowFailureRetention;
        var outbox = await db.Set<FlowOutboxMessage>().Where(x =>
                (x.DispatchedAt != null && x.DispatchedAt < flowSuccessBefore) ||
                (x.DeadLetteredAt != null && x.DeadLetteredAt < flowFailureBefore))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        db.RemoveRange(outbox);

        var inbox = await db.Set<FlowInboxMessage>().Where(x => x.CompletedAt != null && x.CompletedAt < flowSuccessBefore)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        db.RemoveRange(inbox);

        if (items.Count + outbox.Count + inbox.Count > 0)
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
