using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Flow.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LwXLabs.Notifications.AspNetCore;

internal sealed class DeliverNotificationCommand : IMessageCommand
{
    public Guid NotificationId { get; set; }
    public int DeliveryNumber { get; set; }
}

internal sealed class FinalizeAutoDeliveryCommand : IMessageCommand
{
    public Guid NotificationId { get; set; }
    public int DeliveryNumber { get; set; }
}

internal sealed class LocalFlowMessageTransport : IMessageTransport
{
    private readonly MessageProcessor _processor;
    public LocalFlowMessageTransport(MessageProcessor processor) => _processor = processor;

    public async Task Publish(MessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        var outcome = await _processor.Process(envelope, 1, cancellationToken).ConfigureAwait(false);
        if (outcome == MessageProcessingOutcome.Busy)
            throw new InvalidOperationException($"Flow message {envelope.MessageId} is currently busy.");
        if (outcome == MessageProcessingOutcome.UnknownMessageType)
            throw new InvalidOperationException($"Flow message {envelope.MessageId} has an unknown type '{envelope.MessageType}'.");
    }
}

internal sealed class DeliverNotificationHandler : IMessageCommandHandler<DeliverNotificationCommand>
{
    private readonly NotificationDbContext _db;
    private readonly SignalRNotificationTransport _live;
    private readonly INotificationPushTransport _push;
    private readonly PushTargetResolver _targets;
    private readonly IMessageBus _bus;
    private readonly NotificationServerOptions _options;

    public DeliverNotificationHandler(
        NotificationDbContext db,
        SignalRNotificationTransport live,
        INotificationPushTransport push,
        PushTargetResolver targets,
        IMessageBus bus,
        Microsoft.Extensions.Options.IOptions<NotificationServerOptions> options)
    {
        _db = db;
        _live = live;
        _push = push;
        _targets = targets;
        _bus = bus;
        _options = options.Value;
    }

    public async Task Handle(DeliverNotificationCommand command, MessageContext context, CancellationToken cancellationToken = default)
    {
        var item = await _db.Notifications.SingleOrDefaultAsync(x => x.Id == command.NotificationId, cancellationToken).ConfigureAwait(false);
        if (item == null || IsTerminal(item)) return;
        if (item.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            item.Status = NotificationItemStatus.Expired;
            item.CompletedAt = DateTimeOffset.UtcNow;
            return;
        }
        if (command.DeliveryNumber <= item.LastCompletedDeliveryNumber) return;

        var app = await _db.Applications.AsNoTracking().SingleAsync(x => x.Id == item.ApplicationId, cancellationToken).ConfigureAwait(false);
        var envelope = ToEnvelope(app.Key, item, command.DeliveryNumber);

        switch (item.DeliveryMode)
        {
            case NotificationDeliveryMode.Auto:
                await _live.SendAsync(app.Key, item, envelope, cancellationToken).ConfigureAwait(false);
                await _bus.Send(
                    new FinalizeAutoDeliveryCommand { NotificationId = item.Id, DeliveryNumber = command.DeliveryNumber },
                    new MessageOptions
                    {
                        NotBefore = DateTimeOffset.UtcNow.Add(_options.AckGracePeriod),
                        TimeToLive = RemainingLifetime(item)
                    },
                    cancellationToken).ConfigureAwait(false);
                break;

            case NotificationDeliveryMode.Live:
                await _live.SendAsync(app.Key, item, envelope, cancellationToken).ConfigureAwait(false);
                await CompleteOccurrence(item, command.DeliveryNumber, cancellationToken).ConfigureAwait(false);
                break;

            case NotificationDeliveryMode.Push:
                if (await SendPush(app.Key, item, envelope, cancellationToken).ConfigureAwait(false))
                    await CompleteOccurrence(item, command.DeliveryNumber, cancellationToken).ConfigureAwait(false);
                else
                    MarkPushFailure(item);
                break;

            case NotificationDeliveryMode.All:
                await _live.SendAsync(app.Key, item, envelope, cancellationToken).ConfigureAwait(false);
                if (!await SendPush(app.Key, item, envelope, cancellationToken).ConfigureAwait(false))
                    item.LastError = "No push endpoint accepted this delivery.";
                await CompleteOccurrence(item, command.DeliveryNumber, cancellationToken).ConfigureAwait(false);
                break;

            default:
                throw new InvalidOperationException($"Unsupported delivery mode {item.DeliveryMode}.");
        }
    }

    private async Task<bool> SendPush(string appKey, NotificationItemEntity item, NotificationDeliveryEnvelope envelope, CancellationToken cancellationToken)
    {
        var target = new NotificationTarget(item.TargetKind, item.TargetValue);
        var endpoints = await _targets.ResolveAsync(item.ApplicationId, target, cancellationToken).ConfigureAwait(false);
        var result = await _push.SendAsync(new PushSendRequest
        {
            Application = appKey,
            Target = target,
            Envelope = envelope,
            Endpoints = endpoints
        }, cancellationToken).ConfigureAwait(false);
        if (result.InvalidInstallationIds.Count > 0)
            await _targets.DisableAsync(result.InvalidInstallationIds, cancellationToken).ConfigureAwait(false);
        return result.Accepted > 0;
    }

    private static void MarkPushFailure(NotificationItemEntity item)
    {
        item.Status = NotificationItemStatus.Failed;
        item.CompletedAt = DateTimeOffset.UtcNow;
        item.LastError = "No push endpoint accepted this delivery.";
    }

    private async Task CompleteOccurrence(NotificationItemEntity item, int deliveryNumber, CancellationToken cancellationToken)
    {
        if (deliveryNumber <= item.LastCompletedDeliveryNumber) return;

        item.LastCompletedDeliveryNumber = deliveryNumber;
        item.DeliveredCount++;

        var repeat = item.RepeatEveryMilliseconds.HasValue && item.DeliveredCount < item.MaxDeliveries;
        if (repeat)
        {
            var next = DateTimeOffset.UtcNow.AddMilliseconds(item.RepeatEveryMilliseconds!.Value);
            if (next < item.ExpiresAt)
            {
                item.Status = NotificationItemStatus.Scheduled;
                await _bus.Send(
                    new DeliverNotificationCommand { NotificationId = item.Id, DeliveryNumber = deliveryNumber + 1 },
                    new MessageOptions { NotBefore = next, TimeToLive = item.ExpiresAt - DateTimeOffset.UtcNow },
                    cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        item.Status = NotificationItemStatus.Completed;
        item.CompletedAt = DateTimeOffset.UtcNow;
    }

    internal static NotificationDeliveryEnvelope ToEnvelope(string appKey, NotificationItemEntity item, int deliveryNumber) => new()
    {
        Id = item.Id,
        Application = appKey,
        Target = new NotificationTarget(item.TargetKind, item.TargetValue),
        Content = NotificationSerialization.Deserialize<NotificationContent>(item.ContentJson),
        DeliveryNumber = deliveryNumber,
        CreatedAt = item.CreatedAt,
        ExpiresAt = item.ExpiresAt,
        Metadata = NotificationSerialization.Deserialize<Dictionary<string, string>>(item.MetadataJson)
    };

    private static bool IsTerminal(NotificationItemEntity item) =>
        item.Status is NotificationItemStatus.Cancelled or NotificationItemStatus.Completed or NotificationItemStatus.Expired or NotificationItemStatus.Failed;

    private static TimeSpan RemainingLifetime(NotificationItemEntity item)
    {
        var remaining = item.ExpiresAt - DateTimeOffset.UtcNow;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.FromSeconds(1);
    }
}

internal sealed class FinalizeAutoDeliveryHandler : IMessageCommandHandler<FinalizeAutoDeliveryCommand>
{
    private readonly NotificationDbContext _db;
    private readonly INotificationPushTransport _push;
    private readonly PushTargetResolver _targets;
    private readonly IMessageBus _bus;

    public FinalizeAutoDeliveryHandler(NotificationDbContext db, INotificationPushTransport push, PushTargetResolver targets, IMessageBus bus)
    {
        _db = db;
        _push = push;
        _targets = targets;
        _bus = bus;
    }

    public async Task Handle(FinalizeAutoDeliveryCommand command, MessageContext context, CancellationToken cancellationToken = default)
    {
        var item = await _db.Notifications.SingleOrDefaultAsync(x => x.Id == command.NotificationId, cancellationToken).ConfigureAwait(false);
        if (item == null || item.Status is NotificationItemStatus.Cancelled or NotificationItemStatus.Completed or NotificationItemStatus.Expired or NotificationItemStatus.Failed) return;
        if (command.DeliveryNumber <= item.LastCompletedDeliveryNumber) return;
        if (item.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            item.Status = NotificationItemStatus.Expired;
            item.CompletedAt = DateTimeOffset.UtcNow;
            return;
        }

        var app = await _db.Applications.AsNoTracking().SingleAsync(x => x.Id == item.ApplicationId, cancellationToken).ConfigureAwait(false);
        if (item.LastAcknowledgedDeliveryNumber < command.DeliveryNumber)
        {
            var target = new NotificationTarget(item.TargetKind, item.TargetValue);
            var endpoints = await _targets.ResolveAsync(item.ApplicationId, target, cancellationToken).ConfigureAwait(false);
            var result = await _push.SendAsync(new PushSendRequest
            {
                Application = app.Key,
                Target = target,
                Envelope = DeliverNotificationHandler.ToEnvelope(app.Key, item, command.DeliveryNumber),
                Endpoints = endpoints
            }, cancellationToken).ConfigureAwait(false);
            if (result.InvalidInstallationIds.Count > 0)
                await _targets.DisableAsync(result.InvalidInstallationIds, cancellationToken).ConfigureAwait(false);
            if (result.Accepted == 0)
            {
                item.Status = NotificationItemStatus.Failed;
                item.CompletedAt = DateTimeOffset.UtcNow;
                item.LastError = "No push endpoint accepted this delivery.";
                return;
            }
        }

        item.LastCompletedDeliveryNumber = command.DeliveryNumber;
        item.DeliveredCount++;

        if (item.RepeatEveryMilliseconds.HasValue && item.DeliveredCount < item.MaxDeliveries)
        {
            var next = DateTimeOffset.UtcNow.AddMilliseconds(item.RepeatEveryMilliseconds.Value);
            if (next < item.ExpiresAt)
            {
                item.Status = NotificationItemStatus.Scheduled;
                await _bus.Send(
                    new DeliverNotificationCommand { NotificationId = item.Id, DeliveryNumber = command.DeliveryNumber + 1 },
                    new MessageOptions { NotBefore = next, TimeToLive = item.ExpiresAt - DateTimeOffset.UtcNow },
                    cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        item.Status = NotificationItemStatus.Completed;
        item.CompletedAt = DateTimeOffset.UtcNow;
    }
}
