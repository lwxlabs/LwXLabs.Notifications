using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LwXLabs.Notifications.AspNetCore;

public sealed class PushEndpoint
{
    public Guid InstallationDatabaseId { get; set; }
    public string InstallationId { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public PushEndpointKind Kind { get; set; }
}

public sealed class PushSendRequest
{
    public string Application { get; set; } = string.Empty;
    public NotificationTarget Target { get; set; } = new();
    public NotificationDeliveryEnvelope Envelope { get; set; } = new();
    public IReadOnlyList<PushEndpoint> Endpoints { get; set; } = Array.Empty<PushEndpoint>();
}

public sealed class PushSendResult
{
    public int Attempted { get; set; }
    public int Accepted { get; set; }
    public IReadOnlyCollection<Guid> InvalidInstallationIds { get; set; } = Array.Empty<Guid>();
}

public interface INotificationPushTransport
{
    Task<PushSendResult> SendAsync(PushSendRequest request, CancellationToken cancellationToken = default);
    Task SyncTopicsAsync(string application, PushEndpoint endpoint, IReadOnlyCollection<string> addedTopics, IReadOnlyCollection<string> removedTopics, CancellationToken cancellationToken = default);
}

internal sealed class NoPushNotificationTransport : INotificationPushTransport
{
    public Task<PushSendResult> SendAsync(PushSendRequest request, CancellationToken cancellationToken = default) =>
        Task.FromException<PushSendResult>(new InvalidOperationException("No notification push transport is configured."));

    public Task SyncTopicsAsync(string application, PushEndpoint endpoint, IReadOnlyCollection<string> addedTopics, IReadOnlyCollection<string> removedTopics, CancellationToken cancellationToken = default)
    {
        if ((addedTopics.Count > 0 || removedTopics.Count > 0) && endpoint.Kind != PushEndpointKind.None)
            return Task.FromException(new InvalidOperationException("No notification push transport is configured."));
        return Task.CompletedTask;
    }
}

public interface INotificationActionSink
{
    Task<bool> HandleAsync(string application, Guid notificationId, string installationId, NotificationActionInvocation action, CancellationToken cancellationToken = default);
}

internal sealed class NoNotificationActionSink : INotificationActionSink
{
    public Task<bool> HandleAsync(string application, Guid notificationId, string installationId, NotificationActionInvocation action, CancellationToken cancellationToken = default) => Task.FromResult(false);
}
