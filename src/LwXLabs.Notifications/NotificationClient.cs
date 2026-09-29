using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LwXLabs.Notifications;

public sealed class NotificationClientOptions
{
    public Uri? ServerUrl { get; set; }
    public string Application { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
}

public interface INotificationSender
{
    Task<NotificationReceipt> SendAsync(NotificationRequest request, CancellationToken cancellationToken = default);
    Task CancelAsync(Guid notificationId, CancellationToken cancellationToken = default);
    Task<RegisteredInstallation> RegisterInstallationAsync(RegisterInstallationRequest request, CancellationToken cancellationToken = default);
    Task UnregisterInstallationAsync(string installationId, CancellationToken cancellationToken = default);
}

public sealed class NotificationSender : INotificationSender
{
    private readonly HttpClient _httpClient;
    private readonly NotificationClientOptions _options;

    public NotificationSender(HttpClient httpClient, IOptions<NotificationClientOptions> options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        if (_options.ServerUrl == null) throw new InvalidOperationException("Notification ServerUrl is required.");
        if (string.IsNullOrWhiteSpace(_options.Application)) throw new InvalidOperationException("Notification Application is required.");
        if (string.IsNullOrWhiteSpace(_options.ApiKey)) throw new InvalidOperationException("Notification ApiKey is required.");
        _httpClient.BaseAddress ??= _options.ServerUrl;
    }

    public async Task<NotificationReceipt> SendAsync(NotificationRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        using var message = CreateRequest(HttpMethod.Post, $"v1/apps/{Uri.EscapeDataString(_options.Application)}/notifications", request);
        using var response = await _httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
        return await ReadAsync<NotificationReceipt>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task CancelAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        using var message = CreateRequest(HttpMethod.Delete, $"v1/apps/{Uri.EscapeDataString(_options.Application)}/notifications/{notificationId:D}");
        using var response = await _httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RegisteredInstallation> RegisterInstallationAsync(RegisterInstallationRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        using var message = CreateRequest(HttpMethod.Post, $"v1/apps/{Uri.EscapeDataString(_options.Application)}/installations", request);
        using var response = await _httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
        return await ReadAsync<RegisteredInstallation>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task UnregisterInstallationAsync(string installationId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(installationId)) throw new ArgumentException("InstallationId is required.", nameof(installationId));
        using var message = CreateRequest(
            HttpMethod.Delete,
            $"v1/apps/{Uri.EscapeDataString(_options.Application)}/installations/{Uri.EscapeDataString(installationId)}");
        using var response = await _httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string uri, object? body = null)
    {
        var message = new HttpRequestMessage(method, uri);
        message.Headers.TryAddWithoutValidation("X-LwX-Api-Key", _options.ApiKey);
        if (body != null)
        {
            var json = JsonSerializer.Serialize(body, NotificationJson.SerializerOptions);
            message.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }
        return message;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(json, NotificationJson.SerializerOptions)
            ?? throw new InvalidOperationException("Notification server returned an empty response.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        throw new HttpRequestException($"Notification server returned {(int)response.StatusCode} ({response.ReasonPhrase}): {body}");
    }
}

public static class NotificationClientServiceCollectionExtensions
{
    public static IServiceCollection AddLwXLabsNotifications(
        this IServiceCollection services,
        Action<NotificationClientOptions> configure)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        if (configure == null) throw new ArgumentNullException(nameof(configure));

        services.Configure(configure);
        services.AddHttpClient<INotificationSender, NotificationSender>();
        return services;
    }
}
