using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace LwXLabs.Notifications.AspNetCore;

internal sealed class PushTargetResolver
{
    private readonly NotificationDbContext _db;
    public PushTargetResolver(NotificationDbContext db) => _db = db;

    public async Task<IReadOnlyList<PushEndpoint>> ResolveAsync(Guid applicationId, NotificationTarget target, CancellationToken cancellationToken)
    {
        if (target.Kind == NotificationTargetKind.Topic) return Array.Empty<PushEndpoint>();

        var query = _db.Installations.AsNoTracking().Where(x => x.ApplicationId == applicationId && x.Enabled && x.PushEndpoint != null);
        query = target.Kind switch
        {
            NotificationTargetKind.Installation => query.Where(x => x.InstallationId == target.Value),
            NotificationTargetKind.User => query.Where(x => x.UserId == target.Value),
            _ => query
        };

        return await query.Select(x => new PushEndpoint
        {
            InstallationDatabaseId = x.Id,
            InstallationId = x.InstallationId,
            Endpoint = x.PushEndpoint!,
            Kind = x.PushEndpointKind
        }).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DisableAsync(IEnumerable<Guid> installationIds, CancellationToken cancellationToken)
    {
        var ids = installationIds.Distinct().ToArray();
        if (ids.Length == 0) return;
        var items = await _db.Installations.Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var item in items)
        {
            item.PushEndpoint = null;
            item.PushEndpointKind = PushEndpointKind.None;
        }
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
