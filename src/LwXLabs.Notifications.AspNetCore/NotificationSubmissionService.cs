using System;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Flow.Messaging;
using Microsoft.EntityFrameworkCore;

namespace LwXLabs.Notifications.AspNetCore;

internal sealed class NotificationSubmissionService
{
    private static readonly Regex KeyRegex = new("^[A-Za-z0-9._-]{2,64}$", RegexOptions.Compiled);
    private readonly NotificationDbContext _db;
    private readonly IMessageBus _bus;
    private readonly NotificationServerOptions _options;
    private readonly NotificationOutboxSignal _outboxSignal;

    public NotificationSubmissionService(NotificationDbContext db, IMessageBus bus, Microsoft.Extensions.Options.IOptions<NotificationServerOptions> options, NotificationOutboxSignal outboxSignal)
    {
        _db = db;
        _bus = bus;
        _options = options.Value;
        _outboxSignal = outboxSignal;
    }

    public async Task<NotificationReceipt> SubmitAsync(NotificationApplicationEntity app, NotificationRequest request, CancellationToken cancellationToken)
    {
        NormalizeAndValidate(request);
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.Add(request.ExpiresAfter ?? _options.DefaultLifetime);
        var sendAt = request.SendAt ?? now;
        if (sendAt >= expiresAt) throw new ArgumentException("SendAt must be before expiration.", nameof(request));

        if (!string.IsNullOrWhiteSpace(request.DedupeKey))
        {
            var existing = await _db.Notifications.AsNoTracking()
                .SingleOrDefaultAsync(x => x.ApplicationId == app.Id && x.DedupeKey == request.DedupeKey, cancellationToken)
                .ConfigureAwait(false);
            if (existing != null)
                return new NotificationReceipt { Id = existing.Id, AcceptedAt = existing.CreatedAt, Status = existing.Status, Deduplicated = true };
        }

        var notificationId = Guid.NewGuid();
        var normalizedDedupeKey = string.IsNullOrWhiteSpace(request.DedupeKey) ? null : request.DedupeKey.Trim();
        var item = new NotificationItemEntity
        {
            Id = notificationId,
            ApplicationId = app.Id,
            TargetKind = request.Target.Kind,
            TargetValue = request.Target.Value.Trim(),
            ContentJson = NotificationSerialization.Serialize(request.Content),
            MetadataJson = NotificationSerialization.Serialize(request.Metadata),
            DeliveryMode = request.Delivery,
            Status = sendAt > now ? NotificationItemStatus.Scheduled : NotificationItemStatus.Pending,
            CreatedAt = now,
            SendAt = request.SendAt,
            ExpiresAt = expiresAt,
            MaxDeliveries = request.MaxDeliveries,
            RepeatEveryMilliseconds = request.RepeatEvery.HasValue ? (long)request.RepeatEvery.Value.TotalMilliseconds : null,
            DedupeKey = normalizedDedupeKey,
            DedupeIdentity = normalizedDedupeKey == null ? $"id:{notificationId:N}" : $"key:{normalizedDedupeKey}"
        };

        try
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                _db.ChangeTracker.Clear();
                await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                _db.Notifications.Add(item);
                await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await _bus.Send(
                    new DeliverNotificationCommand { NotificationId = item.Id, DeliveryNumber = 1 },
                    new MessageOptions { NotBefore = sendAt > now ? sendAt : null, TimeToLive = expiresAt - now, PartitionKey = app.Key },
                    cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);
            _outboxSignal.Wake();
            return new NotificationReceipt { Id = item.Id, AcceptedAt = now, Status = item.Status };
        }
        catch (DbUpdateException) when (normalizedDedupeKey != null)
        {
            _db.ChangeTracker.Clear();
            var concurrent = await _db.Notifications.AsNoTracking()
                .SingleOrDefaultAsync(x => x.ApplicationId == app.Id && x.DedupeKey == normalizedDedupeKey, cancellationToken)
                .ConfigureAwait(false);
            if (concurrent != null)
                return new NotificationReceipt { Id = concurrent.Id, AcceptedAt = concurrent.CreatedAt, Status = concurrent.Status, Deduplicated = true };
            throw;
        }
    }

    public static bool IsValidApplicationKey(string key) => !string.IsNullOrWhiteSpace(key) && KeyRegex.IsMatch(key);

    private void NormalizeAndValidate(NotificationRequest request)
    {
        if (request.Target == null || string.IsNullOrWhiteSpace(request.Target.Value)) throw new ArgumentException("A notification target is required.", nameof(request));
        if (!Enum.IsDefined(typeof(NotificationTargetKind), request.Target.Kind)) throw new ArgumentException("Notification target kind is invalid.", nameof(request));
        if (request.Target.Value.Trim().Length > 256) throw new ArgumentException("Notification target values are limited to 256 characters.", nameof(request));
        if (!Enum.IsDefined(typeof(NotificationDeliveryMode), request.Delivery)) throw new ArgumentException("Notification delivery mode is invalid.", nameof(request));
        if (request.Content == null) throw new ArgumentException("Notification content is required.", nameof(request));
        request.Content.Components ??= new System.Collections.Generic.List<NotificationComponent>();
        request.Metadata ??= new System.Collections.Generic.Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(request.Content.Title) && string.IsNullOrWhiteSpace(request.Content.Body) && request.Content.Components.Count == 0)
            throw new ArgumentException("A notification needs text or at least one component.", nameof(request));
        if (request.Content.Components.Count > _options.MaxComponents)
            throw new ArgumentException($"At most {_options.MaxComponents} components are allowed.", nameof(request));
        if (request.MaxDeliveries < 1 || request.MaxDeliveries > 100)
            throw new ArgumentOutOfRangeException(nameof(request.MaxDeliveries), "MaxDeliveries must be between 1 and 100.");
        if (request.RepeatEvery.HasValue && request.RepeatEvery <= TimeSpan.Zero)
            throw new ArgumentException("RepeatEvery must be positive.", nameof(request));
        if (request.MaxDeliveries > 1 && !request.RepeatEvery.HasValue)
            throw new ArgumentException("RepeatEvery is required when MaxDeliveries is greater than one.", nameof(request));
        if (request.ExpiresAfter.HasValue && request.ExpiresAfter <= TimeSpan.Zero)
            throw new ArgumentException("ExpiresAfter must be positive.", nameof(request));
        if (request.DedupeKey?.Length > 256) throw new ArgumentException("DedupeKey is limited to 256 characters.", nameof(request));

        foreach (var component in request.Content.Components)
        {
            if (string.IsNullOrWhiteSpace(component.Type)) throw new ArgumentException("Every component requires a type.", nameof(request));
            if (component.Version <= 0) throw new ArgumentException("Every component version must be positive.", nameof(request));
            if (component.Data.ValueKind == JsonValueKind.Undefined) throw new ArgumentException("Every component requires a JSON payload.", nameof(request));
        }

        var bytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(request, NotificationJson.SerializerOptions));
        if (bytes > _options.MaxEnvelopeBytes)
            throw new ArgumentException($"Notification envelope is {bytes} bytes; maximum is {_options.MaxEnvelopeBytes} bytes.", nameof(request));
    }
}
