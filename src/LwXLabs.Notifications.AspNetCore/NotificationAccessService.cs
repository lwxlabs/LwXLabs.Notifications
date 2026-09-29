using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace LwXLabs.Notifications.AspNetCore;

public sealed class NotificationAccessService
{
    private readonly NotificationDbContext _db;

    public NotificationAccessService(NotificationDbContext db) => _db = db;

    public async Task<NotificationApplicationEntity?> AuthenticateApplicationAsync(string appKey, string? apiKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(appKey) || string.IsNullOrWhiteSpace(apiKey)) return null;
        var app = await _db.Applications.SingleOrDefaultAsync(x => x.Key == appKey && x.Enabled, cancellationToken).ConfigureAwait(false);
        return app != null && NotificationSecrets.FixedEquals(apiKey, app.ApiKeyHash) ? app : null;
    }

    public async Task<(NotificationInstallationEntity Installation, NotificationApplicationEntity Application)?> AuthenticateInstallationAsync(string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var hash = NotificationSecrets.Hash(token);
        var installation = await _db.Installations.SingleOrDefaultAsync(x => x.ConnectionTokenHash == hash && x.Enabled, cancellationToken).ConfigureAwait(false);
        if (installation == null) return null;
        var app = await _db.Applications.SingleOrDefaultAsync(x => x.Id == installation.ApplicationId && x.Enabled, cancellationToken).ConfigureAwait(false);
        return app == null ? null : (installation, app);
    }

    public static bool CanAccess(NotificationInstallationEntity installation, NotificationItemEntity item)
    {
        if (installation.ApplicationId != item.ApplicationId) return false;
        return item.TargetKind switch
        {
            NotificationTargetKind.Installation => string.Equals(installation.InstallationId, item.TargetValue, StringComparison.Ordinal),
            NotificationTargetKind.User => !string.IsNullOrWhiteSpace(installation.UserId) && string.Equals(installation.UserId, item.TargetValue, StringComparison.Ordinal),
            NotificationTargetKind.Topic => NotificationSerialization.Topics(installation.TopicsJson).Contains(item.TargetValue, StringComparer.Ordinal),
            _ => false
        };
    }
}
