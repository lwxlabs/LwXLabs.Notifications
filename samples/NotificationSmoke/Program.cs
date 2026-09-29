using LwXLabs.Notifications;
using LwXLabs.Notifications.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

var serverUrl = Environment.GetEnvironmentVariable("NOTIFICATIONS_SERVER_URL") ?? "http://127.0.0.1:8080/";
var appKey = Environment.GetEnvironmentVariable("NOTIFICATIONS_APP_KEY")
    ?? throw new InvalidOperationException("NOTIFICATIONS_APP_KEY is required.");
var apiKey = Environment.GetEnvironmentVariable("NOTIFICATIONS_API_KEY")
    ?? throw new InvalidOperationException("NOTIFICATIONS_API_KEY is required.");

var services = new ServiceCollection();
services.AddLwXLabsNotifications(options =>
{
    options.ServerUrl = new Uri(serverUrl);
    options.Application = appKey;
    options.ApiKey = apiKey;
});
using var provider = services.BuildServiceProvider();
var sender = provider.GetRequiredService<INotificationSender>();

var installationId = "smoke-" + Guid.NewGuid().ToString("N");
var registered = await sender.RegisterInstallationAsync(new RegisterInstallationRequest
{
    InstallationId = installationId,
    UserId = "smoke-user",
    Platform = "console"
});

try
{
    var received = new TaskCompletionSource<NotificationDeliveryEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
    await using (var receiver = new NotificationReceiver(new NotificationReceiverOptions
    {
        ServerUrl = new Uri(serverUrl),
        InstallationToken = registered.ConnectionToken
    }))
    {
        receiver.NotificationReceived += envelope =>
        {
            received.TrySetResult(envelope);
            return Task.CompletedTask;
        };
        await receiver.StartAsync();

        var live = await sender.SendAsync(
            NotificationRequest.ToInstallation(installationId)
                .WithText("SignalR smoke", "Delivered through the installed client")
                .DeliverUsing(NotificationDeliveryMode.Live));
        var envelope = await received.Task.WaitAsync(TimeSpan.FromSeconds(30));
        if (envelope.Id != live.Id || envelope.Application != appKey)
            throw new InvalidOperationException("SignalR delivered the wrong notification.");
        Console.WriteLine($"SignalR received {envelope.Id:D} for {installationId}.");
    }

    // A topic send is accepted by FCM even without a subscribed device. It tests
    // the Firebase transport and credentials, not delivery to physical hardware.
    var topic = "smoke-" + Guid.NewGuid().ToString("N");
    var push = await sender.SendAsync(
        NotificationRequest.ToTopic(topic)
            .WithText("Firebase smoke", "FCM topic transport")
            .DeliverUsing(NotificationDeliveryMode.Push));
    Console.WriteLine($"Firebase topic queued {push.Id:D} for {topic}.");
}
finally
{
    await sender.UnregisterInstallationAsync(installationId);
}
