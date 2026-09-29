using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;

namespace LwXLabs.Notifications.SignalR.Client;

public sealed class NotificationReceiverOptions
{
    public Uri? ServerUrl { get; set; }
    public string InstallationToken { get; set; } = string.Empty;
    public bool AutoAcknowledge { get; set; } = true;
}

public sealed class NotificationReceiver : IAsyncDisposable
{
    private readonly NotificationReceiverOptions _options;
    private readonly HttpClient _httpClient;
    private readonly HubConnection _connection;

    public NotificationReceiver(NotificationReceiverOptions options, HttpMessageHandler? handler = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (_options.ServerUrl == null) throw new ArgumentException("ServerUrl is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(_options.InstallationToken)) throw new ArgumentException("InstallationToken is required.", nameof(options));

        _httpClient = handler == null ? new HttpClient() : new HttpClient(handler);
        _httpClient.BaseAddress = _options.ServerUrl;
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-LwX-Installation-Token", _options.InstallationToken);

        var hubUri = new Uri(_options.ServerUrl, "hubs/notifications");
        _connection = new HubConnectionBuilder()
            .WithUrl(hubUri, connection =>
            {
                connection.Headers["X-LwX-Installation-Token"] = _options.InstallationToken;
            })
            .WithAutomaticReconnect()
            .Build();

        _connection.On<NotificationDeliveryEnvelope>("notification", HandleNotificationAsync);
    }

    public event Func<NotificationDeliveryEnvelope, Task>? NotificationReceived;

    public HubConnectionState State => _connection.State;

    public Task StartAsync(CancellationToken cancellationToken = default) => _connection.StartAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken = default) => _connection.StopAsync(cancellationToken);

    public Task AcknowledgeAsync(Guid notificationId, int deliveryNumber, CancellationToken cancellationToken = default) =>
        _connection.InvokeAsync("Acknowledge", notificationId, deliveryNumber, cancellationToken);

    public Task<NotificationDeliveryEnvelope?> FetchAsync(Guid notificationId, CancellationToken cancellationToken = default) =>
        FetchAsync(notificationId, null, cancellationToken);

    public async Task<NotificationDeliveryEnvelope?> FetchAsync(Guid notificationId, int? deliveryNumber, CancellationToken cancellationToken = default)
    {
        var path = $"v1/receiver/notifications/{notificationId:D}";
        if (deliveryNumber.HasValue) path += $"?deliveryNumber={deliveryNumber.Value}";
        using var response = await _httpClient.GetAsync(path, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<NotificationDeliveryEnvelope>(NotificationJson.SerializerOptions, cancellationToken).ConfigureAwait(false);
    }

    public async Task AcknowledgeHttpAsync(Guid notificationId, int deliveryNumber, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            $"v1/receiver/notifications/{notificationId:D}/ack",
            new NotificationAcknowledgement { DeliveryNumber = deliveryNumber },
            NotificationJson.SerializerOptions,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public Task InvokeActionAsync(Guid notificationId, NotificationActionInvocation action, CancellationToken cancellationToken = default)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));
        return _connection.InvokeAsync("InvokeAction", notificationId, action, cancellationToken);
    }

    public async Task InvokeActionHttpAsync(Guid notificationId, NotificationActionInvocation action, CancellationToken cancellationToken = default)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));
        using var response = await _httpClient.PostAsJsonAsync(
            $"v1/receiver/notifications/{notificationId:D}/actions",
            action,
            NotificationJson.SerializerOptions,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private async Task HandleNotificationAsync(NotificationDeliveryEnvelope envelope)
    {
        var handler = NotificationReceived;
        if (handler != null)
        {
            foreach (Func<NotificationDeliveryEnvelope, Task> subscriber in handler.GetInvocationList())
                await subscriber(envelope).ConfigureAwait(false);
        }

        if (_options.AutoAcknowledge)
            await AcknowledgeAsync(envelope.Id, envelope.DeliveryNumber).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
        _httpClient.Dispose();
    }
}
