using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace LwXLabs.Notifications;

public enum NotificationDeliveryMode
{
    Auto = 0,
    Live = 1,
    Push = 2,
    All = 3
}

public enum NotificationTargetKind
{
    User = 0,
    Installation = 1,
    Topic = 2
}

public enum PushEndpointKind
{
    None = 0,
    FirebaseInstallationId = 1,
    FirebaseRegistrationToken = 2
}

public enum NotificationItemStatus
{
    Pending = 0,
    Scheduled = 1,
    Completed = 2,
    Cancelled = 3,
    Expired = 4,
    Failed = 5
}

public sealed class NotificationTarget
{
    public NotificationTarget() { }

    public NotificationTarget(NotificationTargetKind kind, string value)
    {
        Kind = kind;
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public NotificationTargetKind Kind { get; set; }
    public string Value { get; set; } = string.Empty;

    public static NotificationTarget User(string userId) => new(NotificationTargetKind.User, userId);
    public static NotificationTarget Installation(string installationId) => new(NotificationTargetKind.Installation, installationId);
    public static NotificationTarget Topic(string topic) => new(NotificationTargetKind.Topic, topic);
}

public sealed class NotificationActionDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string? Style { get; set; }
}

public sealed class NotificationComponent
{
    public string Type { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public JsonElement Data { get; set; }

    public static NotificationComponent Create<T>(string type, T data, int version = 1)
    {
        if (string.IsNullOrWhiteSpace(type))
            throw new ArgumentException("A component type is required.", nameof(type));
        if (version <= 0)
            throw new ArgumentOutOfRangeException(nameof(version));

        return new NotificationComponent
        {
            Type = type.Trim(),
            Version = version,
            Data = ToElement(data)
        };
    }

    public static NotificationComponent Progress(string title, double value, string? text = null) =>
        Create("progress", new { title, value, text });

    public static NotificationComponent Status(string title, string status, string? severity = null, string? text = null) =>
        Create("status", new { title, status, severity, text });

    public static NotificationComponent Metric(string label, string value, string? trend = null) =>
        Create("metric", new { label, value, trend });

    public static NotificationComponent Actions(params NotificationActionDefinition[] actions) =>
        Create("actions", new { actions = actions ?? Array.Empty<NotificationActionDefinition>() });

    private static JsonElement ToElement<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, NotificationJson.SerializerOptions);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

public sealed class NotificationContent
{
    public string? Title { get; set; }
    public string? Body { get; set; }
    public List<NotificationComponent> Components { get; set; } = new();

    public static NotificationContent Text(string title, string body) => new()
    {
        Title = title,
        Body = body
    };

    public static NotificationContent Component(NotificationComponent component, string? fallbackTitle = null, string? fallbackBody = null) => new()
    {
        Title = fallbackTitle,
        Body = fallbackBody,
        Components = new List<NotificationComponent> { component ?? throw new ArgumentNullException(nameof(component)) }
    };
}

public sealed class NotificationRequest
{
    public NotificationRequest() { }

    public NotificationRequest(NotificationTarget target)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
    }

    public NotificationTarget Target { get; set; } = new();
    public NotificationContent Content { get; set; } = new();
    public NotificationDeliveryMode Delivery { get; set; } = NotificationDeliveryMode.Auto;
    public DateTimeOffset? SendAt { get; set; }
    public TimeSpan? ExpiresAfter { get; set; }
    public TimeSpan? RepeatEvery { get; set; }
    public int MaxDeliveries { get; set; } = 1;
    public string? DedupeKey { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();

    public static NotificationRequest ToUser(string userId) => new(NotificationTarget.User(userId));
    public static NotificationRequest ToInstallation(string installationId) => new(NotificationTarget.Installation(installationId));
    public static NotificationRequest ToTopic(string topic) => new(NotificationTarget.Topic(topic));

    public NotificationRequest WithText(string title, string body)
    {
        Content.Title = title;
        Content.Body = body;
        return this;
    }

    public NotificationRequest AddComponent(NotificationComponent component)
    {
        Content.Components.Add(component ?? throw new ArgumentNullException(nameof(component)));
        return this;
    }

    public NotificationRequest DeliverUsing(NotificationDeliveryMode mode)
    {
        Delivery = mode;
        return this;
    }

    public NotificationRequest Schedule(DateTimeOffset sendAt)
    {
        SendAt = sendAt;
        return this;
    }

    public NotificationRequest ExpireAfter(TimeSpan lifetime)
    {
        ExpiresAfter = lifetime;
        return this;
    }

    public NotificationRequest Repeat(TimeSpan every, int maximumDeliveries)
    {
        RepeatEvery = every;
        MaxDeliveries = maximumDeliveries;
        return this;
    }

    public NotificationRequest DeduplicateBy(string key)
    {
        DedupeKey = key;
        return this;
    }
}

public sealed class NotificationReceipt
{
    public Guid Id { get; set; }
    public DateTimeOffset AcceptedAt { get; set; }
    public NotificationItemStatus Status { get; set; }
    public bool Deduplicated { get; set; }
}

public sealed class NotificationDeliveryEnvelope
{
    public Guid Id { get; set; }
    public string Application { get; set; } = string.Empty;
    public NotificationTarget Target { get; set; } = new();
    public NotificationContent Content { get; set; } = new();
    public int DeliveryNumber { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}

public sealed class RegisterInstallationRequest
{
    public string InstallationId { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? Platform { get; set; }
    public string? PushEndpoint { get; set; }
    public PushEndpointKind PushEndpointKind { get; set; }
    public List<string> Topics { get; set; } = new();
}

public sealed class RegisteredInstallation
{
    public string InstallationId { get; set; } = string.Empty;
    public string ConnectionToken { get; set; } = string.Empty;
    public DateTimeOffset RegisteredAt { get; set; }
}

public sealed class NotificationAcknowledgement
{
    public int DeliveryNumber { get; set; }
}

public sealed class NotificationActionInvocation
{
    public string ActionId { get; set; } = string.Empty;
    public JsonElement? Data { get; set; }
}

public static class NotificationJson
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
}

public static class NotificationTopics
{
    public static string ToTransportTopic(string appKey, string logicalTopic)
    {
        if (string.IsNullOrWhiteSpace(appKey)) throw new ArgumentException("Application key is required.", nameof(appKey));
        if (string.IsNullOrWhiteSpace(logicalTopic)) throw new ArgumentException("Topic is required.", nameof(logicalTopic));

        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(appKey.Trim() + ":" + logicalTopic.Trim());
        var hash = sha.ComputeHash(bytes);
        var hex = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        return "lwx_" + hex.Substring(0, 40);
    }
}
