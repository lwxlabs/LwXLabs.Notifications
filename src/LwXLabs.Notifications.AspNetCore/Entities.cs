using System;
using LwXLabs.Notifications;

namespace LwXLabs.Notifications.AspNetCore;

public sealed class NotificationApplicationEntity
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ApiKeyHash { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class NotificationInstallationEntity
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public string InstallationId { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? Platform { get; set; }
    public string? PushEndpoint { get; set; }
    public PushEndpointKind PushEndpointKind { get; set; }
    public string TopicsJson { get; set; } = "[]";
    public string ConnectionTokenHash { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}

public sealed class NotificationItemEntity
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public NotificationTargetKind TargetKind { get; set; }
    public string TargetValue { get; set; } = string.Empty;
    public string ContentJson { get; set; } = "{}";
    public string MetadataJson { get; set; } = "{}";
    public NotificationDeliveryMode DeliveryMode { get; set; }
    public NotificationItemStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SendAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int MaxDeliveries { get; set; }
    public int DeliveredCount { get; set; }
    public int LastCompletedDeliveryNumber { get; set; }
    public int LastAcknowledgedDeliveryNumber { get; set; }
    public long? RepeatEveryMilliseconds { get; set; }
    public string? DedupeKey { get; set; }
    public string DedupeIdentity { get; set; } = string.Empty;
    public string? LastError { get; set; }
}
