using System;
using System.Text;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace LwXLabs.Notifications.AspNetCore;

internal static class NotificationGroups
{
    public static string Installation(string app, string installation) => Group("i", app, installation);
    public static string User(string app, string user) => Group("u", app, user);
    public static string Topic(string app, string topic) => Group("t", app, topic);

    public static string ForTarget(string app, NotificationTargetKind kind, string value) => kind switch
    {
        NotificationTargetKind.Installation => Installation(app, value),
        NotificationTargetKind.User => User(app, value),
        NotificationTargetKind.Topic => Topic(app, value),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static string Group(string prefix, string app, string value)
    {
        using var sha = SHA256.Create();
        var raw = Encoding.UTF8.GetBytes(app + ":" + value);
        var hash = Convert.ToBase64String(sha.ComputeHash(raw)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return $"lwx:{prefix}:{hash}";
    }
}

internal sealed class SignalRNotificationTransport
{
    private readonly IHubContext<NotificationHub> _hub;
    public SignalRNotificationTransport(IHubContext<NotificationHub> hub) => _hub = hub;

    public Task SendAsync(string application, NotificationItemEntity item, NotificationDeliveryEnvelope envelope, CancellationToken cancellationToken) =>
        _hub.Clients.Group(NotificationGroups.ForTarget(application, item.TargetKind, item.TargetValue))
            .SendAsync("notification", envelope, cancellationToken);
}

public sealed class NotificationHub : Hub
{
    private readonly NotificationAccessService _access;
    private readonly NotificationDbContext _db;
    private readonly INotificationActionSink _actionSink;

    public NotificationHub(NotificationAccessService access, NotificationDbContext db, INotificationActionSink actionSink)
    {
        _access = access;
        _db = db;
        _actionSink = actionSink;
    }

    public override async Task OnConnectedAsync()
    {
        var http = Context.GetHttpContext();
        var token = http?.Request.Headers["X-LwX-Installation-Token"].ToString();
        if (string.IsNullOrWhiteSpace(token))
            token = http?.Request.Query["token"].ToString(); // browser-compatible fallback
        var authenticated = await _access.AuthenticateInstallationAsync(token, Context.ConnectionAborted).ConfigureAwait(false);
        if (authenticated == null)
        {
            Context.Abort();
            return;
        }

        var (installation, app) = authenticated.Value;
        Context.Items["installation-id"] = installation.Id;
        Context.Items["app-key"] = app.Key;

        await Groups.AddToGroupAsync(Context.ConnectionId, NotificationGroups.Installation(app.Key, installation.InstallationId), Context.ConnectionAborted).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(installation.UserId))
            await Groups.AddToGroupAsync(Context.ConnectionId, NotificationGroups.User(app.Key, installation.UserId), Context.ConnectionAborted).ConfigureAwait(false);
        foreach (var topic in NotificationSerialization.Topics(installation.TopicsJson))
            await Groups.AddToGroupAsync(Context.ConnectionId, NotificationGroups.Topic(app.Key, topic), Context.ConnectionAborted).ConfigureAwait(false);

        installation.LastSeenAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(Context.ConnectionAborted).ConfigureAwait(false);
        await base.OnConnectedAsync().ConfigureAwait(false);
    }

    public async Task Acknowledge(Guid notificationId, int deliveryNumber)
    {
        var installation = await GetInstallation().ConfigureAwait(false);
        var item = await _db.Notifications.SingleOrDefaultAsync(x => x.Id == notificationId, Context.ConnectionAborted).ConfigureAwait(false);
        if (item == null || !NotificationAccessService.CanAccess(installation, item)) throw new HubException("Notification not found.");
        if (deliveryNumber <= 0 || deliveryNumber > item.MaxDeliveries || deliveryNumber > item.LastCompletedDeliveryNumber + 1)
            throw new HubException("Delivery number is invalid.");
        if (deliveryNumber > item.LastAcknowledgedDeliveryNumber)
        {
            item.LastAcknowledgedDeliveryNumber = deliveryNumber;
            await _db.SaveChangesAsync(Context.ConnectionAborted).ConfigureAwait(false);
        }
    }

    public async Task InvokeAction(Guid notificationId, NotificationActionInvocation action)
    {
        var installation = await GetInstallation().ConfigureAwait(false);
        var item = await _db.Notifications.SingleOrDefaultAsync(x => x.Id == notificationId, Context.ConnectionAborted).ConfigureAwait(false);
        if (item == null || !NotificationAccessService.CanAccess(installation, item)) throw new HubException("Notification not found.");
        var appKey = (string)Context.Items["app-key"]!;
        var handled = await _actionSink.HandleAsync(appKey, notificationId, installation.InstallationId, action, Context.ConnectionAborted).ConfigureAwait(false);
        if (!handled) throw new HubException("No notification action sink is configured.");
    }

    private async Task<NotificationInstallationEntity> GetInstallation()
    {
        if (!Context.Items.TryGetValue("installation-id", out var value) || value is not Guid id)
            throw new HubException("Installation is not authenticated.");
        return await _db.Installations.SingleAsync(x => x.Id == id, Context.ConnectionAborted).ConfigureAwait(false);
    }
}
