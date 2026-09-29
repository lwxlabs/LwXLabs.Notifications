using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using LwXLabs.Notifications.AspNetCore;
using Microsoft.Extensions.Options;

namespace LwXLabs.Notifications.Firebase;

internal sealed class FirebasePushTransport : INotificationPushTransport, IDisposable
{
    private readonly FirebaseNotificationsOptions _options;
    private readonly ConcurrentDictionary<string, FirebaseApp> _apps = new(StringComparer.Ordinal);

    public FirebasePushTransport(IOptions<FirebaseNotificationsOptions> options) => _options = options.Value;

    public async Task<PushSendResult> SendAsync(PushSendRequest request, CancellationToken cancellationToken = default)
    {
        var projectId = GetProjectId(request.Application);

        var messaging = FirebaseMessaging.GetMessaging(GetApp(request.Application, projectId));
        var invalid = new List<Guid>();
        var attempted = 0;
        var accepted = 0;

        if (request.Target.Kind == NotificationTargetKind.Topic)
        {
            attempted = 1;
            await messaging.SendAsync(CreateMessage(request, topic: NotificationTopics.ToTransportTopic(request.Application, request.Target.Value)), cancellationToken).ConfigureAwait(false);
            accepted = 1;
        }
        else
        {
            foreach (var endpoint in request.Endpoints)
            {
                attempted++;
                try
                {
                    await messaging.SendAsync(CreateMessage(request, endpoint), cancellationToken).ConfigureAwait(false);
                    accepted++;
                }
                catch (FirebaseMessagingException ex) when (
                    ex.MessagingErrorCode == MessagingErrorCode.Unregistered ||
                    (ex.MessagingErrorCode == MessagingErrorCode.InvalidArgument &&
                     ex.Message.IndexOf("registration token", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    invalid.Add(endpoint.InstallationDatabaseId);
                }
            }
        }

        return new PushSendResult { Attempted = attempted, Accepted = accepted, InvalidInstallationIds = invalid };
    }

    public async Task SyncTopicsAsync(string application, PushEndpoint endpoint, IReadOnlyCollection<string> addedTopics, IReadOnlyCollection<string> removedTopics, CancellationToken cancellationToken = default)
    {
        var projectId = GetProjectId(application);
        if (string.IsNullOrWhiteSpace(endpoint.Endpoint)) return;

        var messaging = FirebaseMessaging.GetMessaging(GetApp(application, projectId));
        foreach (var topic in addedTopics)
            await messaging.SubscribeToTopicAsync(new[] { endpoint.Endpoint }, NotificationTopics.ToTransportTopic(application, topic), cancellationToken).ConfigureAwait(false);
        foreach (var topic in removedTopics)
            await messaging.UnsubscribeFromTopicAsync(new[] { endpoint.Endpoint }, NotificationTopics.ToTransportTopic(application, topic), cancellationToken).ConfigureAwait(false);
    }

    private Message CreateMessage(PushSendRequest request, PushEndpoint? endpoint = null, string? topic = null)
    {
        var envelopeJson = JsonSerializer.Serialize(request.Envelope, NotificationJson.SerializerOptions);
        var inline = Encoding.UTF8.GetByteCount(envelopeJson) <= _options.InlinePayloadByteLimit;
        var data = new Dictionary<string, string>
        {
            ["lwx_id"] = request.Envelope.Id.ToString("D"),
            ["lwx_delivery"] = request.Envelope.DeliveryNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["lwx_app"] = request.Application,
            ["lwx_fetch"] = inline ? "0" : "1"
        };
        if (inline) data["lwx_payload"] = envelopeJson;

        var message = new Message { Data = data, Topic = topic };
        if (endpoint != null)
        {
            if (endpoint.Kind == PushEndpointKind.FirebaseInstallationId)
                message.Fid = endpoint.Endpoint;
            else
#pragma warning disable CS0618
                message.Token = endpoint.Endpoint;
#pragma warning restore CS0618
        }

        if (!string.IsNullOrWhiteSpace(request.Envelope.Content.Title) || !string.IsNullOrWhiteSpace(request.Envelope.Content.Body))
        {
            message.Notification = new FirebaseAdmin.Messaging.Notification
            {
                Title = request.Envelope.Content.Title,
                Body = request.Envelope.Content.Body
            };
        }
        return message;
    }

    private string GetProjectId(string appKey)
    {
        if (_options.Applications.TryGetValue(appKey, out var app) && !string.IsNullOrWhiteSpace(app.ProjectId))
            return app.ProjectId;
        if (!string.IsNullOrWhiteSpace(_options.ProjectId)) return _options.ProjectId;
        throw new InvalidOperationException($"Firebase is not configured for notification application '{appKey}'.");
    }

    private FirebaseApp GetApp(string appKey, string projectId) => _apps.GetOrAdd(appKey, _ =>
    {
        return FirebaseApp.Create(
            new AppOptions { Credential = GoogleCredential.GetApplicationDefault(), ProjectId = projectId },
            "lwx-notifications-" + NotificationTopics.ToTransportTopic("firebase", appKey));
    });

    public void Dispose()
    {
        foreach (var app in _apps.Values) app.Delete();
        _apps.Clear();
    }
}
