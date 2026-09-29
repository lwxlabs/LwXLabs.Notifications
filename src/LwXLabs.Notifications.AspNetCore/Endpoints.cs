using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LwXLabs.Notifications.AspNetCore;

public sealed class CreateNotificationApplicationRequest
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class CreatedNotificationApplication
{
    public string Key { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
}

public static class NotificationEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapLwXLabsNotifications(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/v1/admin/apps", CreateApplication);
        endpoints.MapPost("/v1/admin/apps/{appKey}/rotate-key", RotateApplicationKey);
        endpoints.MapPost("/v1/apps/{appKey}/notifications", SendNotification);
        endpoints.MapDelete("/v1/apps/{appKey}/notifications/{notificationId:guid}", CancelNotification);
        endpoints.MapPost("/v1/apps/{appKey}/installations", RegisterInstallation);
        endpoints.MapDelete("/v1/apps/{appKey}/installations/{installationId}", UnregisterInstallation);
        endpoints.MapGet("/v1/receiver/notifications/{notificationId:guid}", GetReceiverNotification);
        endpoints.MapPost("/v1/receiver/notifications/{notificationId:guid}/ack", AcknowledgeReceiverNotification);
        endpoints.MapPost("/v1/receiver/notifications/{notificationId:guid}/actions", InvokeReceiverAction);
        endpoints.MapHub<NotificationHub>("/hubs/notifications");
        return endpoints;
    }

    private static async Task<IResult> CreateApplication(HttpRequest request, CreateNotificationApplicationRequest body, NotificationDbContext db, IOptions<NotificationServerOptions> options, CancellationToken cancellationToken)
    {
        if (!AdminAuthorized(request, options.Value)) return Results.Unauthorized();
        if (!NotificationSubmissionService.IsValidApplicationKey(body.Key)) return Results.BadRequest(new { error = "Application key must be 2-64 characters using letters, numbers, '.', '_' or '-'." });
        if (string.IsNullOrWhiteSpace(body.Name)) return Results.BadRequest(new { error = "Name is required." });
        if (body.Name.Trim().Length > 200) return Results.BadRequest(new { error = "Name is limited to 200 characters." });
        if (await db.Applications.AnyAsync(x => x.Key == body.Key, cancellationToken).ConfigureAwait(false)) return Results.Conflict(new { error = "Application already exists." });

        var apiKey = NotificationSecrets.Generate();
        db.Applications.Add(new NotificationApplicationEntity
        {
            Id = Guid.NewGuid(),
            Key = body.Key.Trim(),
            Name = body.Name.Trim(),
            ApiKeyHash = NotificationSecrets.Hash(apiKey),
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(new CreatedNotificationApplication { Key = body.Key.Trim(), ApiKey = apiKey });
    }

    private static async Task<IResult> RotateApplicationKey(string appKey, HttpRequest request, NotificationDbContext db, IOptions<NotificationServerOptions> options, CancellationToken cancellationToken)
    {
        if (!AdminAuthorized(request, options.Value)) return Results.Unauthorized();
        var app = await db.Applications.SingleOrDefaultAsync(x => x.Key == appKey, cancellationToken).ConfigureAwait(false);
        if (app == null) return Results.NotFound();
        var apiKey = NotificationSecrets.Generate();
        app.ApiKeyHash = NotificationSecrets.Hash(apiKey);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(new CreatedNotificationApplication { Key = app.Key, ApiKey = apiKey });
    }

    private static async Task<IResult> SendNotification(string appKey, HttpRequest http, NotificationRequest body, NotificationAccessService access, NotificationSubmissionService sender, NotificationApplicationQuota quota, CancellationToken cancellationToken)
    {
        var app = await access.AuthenticateApplicationAsync(appKey, http.Headers["X-LwX-Api-Key"].ToString(), cancellationToken).ConfigureAwait(false);
        if (app == null) return Results.Unauthorized();
        if (!quota.TrySend(app.Id)) return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        try { return Results.Accepted(value: await sender.SubmitAsync(app, body, cancellationToken).ConfigureAwait(false)); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    }

    private static async Task<IResult> CancelNotification(string appKey, Guid notificationId, HttpRequest http, NotificationAccessService access, NotificationDbContext db, CancellationToken cancellationToken)
    {
        var app = await access.AuthenticateApplicationAsync(appKey, http.Headers["X-LwX-Api-Key"].ToString(), cancellationToken).ConfigureAwait(false);
        if (app == null) return Results.Unauthorized();
        var item = await db.Notifications.SingleOrDefaultAsync(x => x.Id == notificationId && x.ApplicationId == app.Id, cancellationToken).ConfigureAwait(false);
        if (item == null) return Results.NotFound();
        item.Status = NotificationItemStatus.Cancelled;
        item.CompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> RegisterInstallation(string appKey, HttpRequest http, RegisterInstallationRequest body, NotificationAccessService access, InstallationService service, NotificationApplicationQuota quota, CancellationToken cancellationToken)
    {
        var app = await access.AuthenticateApplicationAsync(appKey, http.Headers["X-LwX-Api-Key"].ToString(), cancellationToken).ConfigureAwait(false);
        if (app == null) return Results.Unauthorized();
        if (!quota.TryRegister(app.Id)) return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        try { return Results.Ok(await service.RegisterAsync(app, body, cancellationToken).ConfigureAwait(false)); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    }

    private static async Task<IResult> UnregisterInstallation(string appKey, string installationId, HttpRequest http, NotificationAccessService access, InstallationService service, CancellationToken cancellationToken)
    {
        var app = await access.AuthenticateApplicationAsync(appKey, http.Headers["X-LwX-Api-Key"].ToString(), cancellationToken).ConfigureAwait(false);
        if (app == null) return Results.Unauthorized();
        try
        {
            var removed = await service.UnregisterAsync(app, installationId, cancellationToken).ConfigureAwait(false);
            return removed ? Results.NoContent() : Results.NotFound();
        }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    }

    private static async Task<IResult> GetReceiverNotification(Guid notificationId, int? deliveryNumber, HttpRequest http, NotificationAccessService access, NotificationDbContext db, CancellationToken cancellationToken)
    {
        var auth = await access.AuthenticateInstallationAsync(http.Headers["X-LwX-Installation-Token"].ToString(), cancellationToken).ConfigureAwait(false);
        if (auth == null) return Results.Unauthorized();
        var (installation, app) = auth.Value;
        var item = await db.Notifications.AsNoTracking().SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken).ConfigureAwait(false);
        if (item == null || !NotificationAccessService.CanAccess(installation, item)) return Results.NotFound();
        if (deliveryNumber is <= 0) return Results.BadRequest(new { error = "deliveryNumber must be positive." });
        var effectiveDeliveryNumber = deliveryNumber ?? Math.Max(1, item.LastCompletedDeliveryNumber + (item.Status == NotificationItemStatus.Completed ? 0 : 1));
        if (effectiveDeliveryNumber > item.MaxDeliveries) return Results.BadRequest(new { error = "deliveryNumber exceeds MaxDeliveries." });
        return Results.Ok(DeliverNotificationHandler.ToEnvelope(app.Key, item, effectiveDeliveryNumber));
    }

    private static async Task<IResult> AcknowledgeReceiverNotification(Guid notificationId, HttpRequest http, NotificationAcknowledgement body, NotificationAccessService access, NotificationDbContext db, CancellationToken cancellationToken)
    {
        var auth = await access.AuthenticateInstallationAsync(http.Headers["X-LwX-Installation-Token"].ToString(), cancellationToken).ConfigureAwait(false);
        if (auth == null) return Results.Unauthorized();
        var (installation, _) = auth.Value;
        var item = await db.Notifications.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken).ConfigureAwait(false);
        if (item == null || !NotificationAccessService.CanAccess(installation, item)) return Results.NotFound();
        if (body.DeliveryNumber <= 0 || body.DeliveryNumber > item.MaxDeliveries || body.DeliveryNumber > item.LastCompletedDeliveryNumber + 1)
            return Results.BadRequest(new { error = "DeliveryNumber is invalid." });
        if (body.DeliveryNumber > item.LastAcknowledgedDeliveryNumber)
        {
            item.LastAcknowledgedDeliveryNumber = body.DeliveryNumber;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        return Results.NoContent();
    }

    private static async Task<IResult> InvokeReceiverAction(Guid notificationId, HttpRequest http, NotificationActionInvocation body, NotificationAccessService access, NotificationDbContext db, INotificationActionSink sink, CancellationToken cancellationToken)
    {
        var auth = await access.AuthenticateInstallationAsync(http.Headers["X-LwX-Installation-Token"].ToString(), cancellationToken).ConfigureAwait(false);
        if (auth == null) return Results.Unauthorized();
        var (installation, app) = auth.Value;
        var item = await db.Notifications.AsNoTracking().SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken).ConfigureAwait(false);
        if (item == null || !NotificationAccessService.CanAccess(installation, item)) return Results.NotFound();
        if (string.IsNullOrWhiteSpace(body.ActionId)) return Results.BadRequest(new { error = "ActionId is required." });
        var handled = await sink.HandleAsync(app.Key, notificationId, installation.InstallationId, body, cancellationToken).ConfigureAwait(false);
        return handled ? Results.Accepted() : Results.StatusCode(StatusCodes.Status501NotImplemented);
    }

    private static bool AdminAuthorized(HttpRequest request, NotificationServerOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.AdminKey)) return false;
        var supplied = request.Headers["X-LwX-Admin-Key"].ToString();
        return !string.IsNullOrWhiteSpace(supplied) && NotificationSecrets.FixedRawEquals(supplied, options.AdminKey);
    }
}
