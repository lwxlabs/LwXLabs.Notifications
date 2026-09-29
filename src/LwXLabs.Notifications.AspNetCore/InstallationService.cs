using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace LwXLabs.Notifications.AspNetCore;

internal sealed class InstallationService
{
    private readonly NotificationDbContext _db;
    private readonly INotificationPushTransport _push;

    public InstallationService(NotificationDbContext db, INotificationPushTransport push)
    {
        _db = db;
        _push = push;
    }

    public async Task<RegisteredInstallation> RegisterAsync(NotificationApplicationEntity app, RegisterInstallationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.InstallationId)) throw new ArgumentException("InstallationId is required.", nameof(request));
        if (request.InstallationId.Length > 200) throw new ArgumentException("InstallationId is limited to 200 characters.", nameof(request));
        if (!string.IsNullOrWhiteSpace(request.UserId) && request.UserId.Trim().Length > 200) throw new ArgumentException("UserId is limited to 200 characters.", nameof(request));
        if (!string.IsNullOrWhiteSpace(request.Platform) && request.Platform.Trim().Length > 64) throw new ArgumentException("Platform is limited to 64 characters.", nameof(request));
        if (!string.IsNullOrWhiteSpace(request.PushEndpoint) && request.PushEndpoint.Trim().Length > 2048) throw new ArgumentException("PushEndpoint is limited to 2048 characters.", nameof(request));
        if (!Enum.IsDefined(typeof(PushEndpointKind), request.PushEndpointKind)) throw new ArgumentException("PushEndpointKind is invalid.", nameof(request));
        if (!string.IsNullOrWhiteSpace(request.PushEndpoint) && request.PushEndpointKind == PushEndpointKind.None)
            throw new ArgumentException("PushEndpointKind is required when PushEndpoint is set.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.PushEndpoint) && request.PushEndpointKind != PushEndpointKind.None)
            throw new ArgumentException("PushEndpoint is required when PushEndpointKind is set.", nameof(request));

        request.Topics ??= new List<string>();
        var normalizedTopics = request.Topics.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
        if (normalizedTopics.Count > 20) throw new ArgumentException("An installation can subscribe to at most 20 topics.", nameof(request));
        if (normalizedTopics.Any(x => x.Length > 200)) throw new ArgumentException("Topics are limited to 200 characters.", nameof(request));

        var existing = await _db.Installations.SingleOrDefaultAsync(
            x => x.ApplicationId == app.Id && x.InstallationId == request.InstallationId,
            cancellationToken).ConfigureAwait(false);

        var oldTopics = existing == null ? new List<string>() : NotificationSerialization.Topics(existing.TopicsJson);
        var oldEndpoint = existing?.PushEndpoint;
        var oldKind = existing?.PushEndpointKind ?? PushEndpointKind.None;
        var token = NotificationSecrets.Generate();
        var now = DateTimeOffset.UtcNow;

        if (existing == null)
        {
            existing = new NotificationInstallationEntity
            {
                Id = Guid.NewGuid(),
                ApplicationId = app.Id,
                InstallationId = request.InstallationId.Trim(),
                CreatedAt = now
            };
            _db.Installations.Add(existing);
        }

        existing.UserId = string.IsNullOrWhiteSpace(request.UserId) ? null : request.UserId.Trim();
        existing.Platform = string.IsNullOrWhiteSpace(request.Platform) ? null : request.Platform.Trim();
        existing.PushEndpoint = string.IsNullOrWhiteSpace(request.PushEndpoint) ? null : request.PushEndpoint.Trim();
        existing.PushEndpointKind = existing.PushEndpoint == null ? PushEndpointKind.None : request.PushEndpointKind;
        existing.TopicsJson = NotificationSerialization.Serialize(normalizedTopics);
        existing.ConnectionTokenHash = NotificationSecrets.Hash(token);
        existing.Enabled = true;
        existing.LastSeenAt = now;

        var endpointChanged = !string.Equals(oldEndpoint, existing.PushEndpoint, StringComparison.Ordinal) || oldKind != existing.PushEndpointKind;
        if (endpointChanged && !string.IsNullOrWhiteSpace(oldEndpoint))
        {
            await _push.SyncTopicsAsync(
                app.Key,
                new PushEndpoint
                {
                    InstallationDatabaseId = existing.Id,
                    InstallationId = existing.InstallationId,
                    Endpoint = oldEndpoint,
                    Kind = oldKind
                },
                Array.Empty<string>(),
                oldTopics.ToArray(),
                cancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(existing.PushEndpoint))
        {
            var endpoint = new PushEndpoint
            {
                InstallationDatabaseId = existing.Id,
                InstallationId = existing.InstallationId,
                Endpoint = existing.PushEndpoint,
                Kind = existing.PushEndpointKind
            };
            var added = endpointChanged ? normalizedTopics : normalizedTopics.Except(oldTopics, StringComparer.Ordinal).ToList();
            var removed = endpointChanged ? new List<string>() : oldTopics.Except(normalizedTopics, StringComparer.Ordinal).ToList();
            await _push.SyncTopicsAsync(app.Key, endpoint, added.ToArray(), removed.ToArray(), cancellationToken).ConfigureAwait(false);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new RegisteredInstallation { InstallationId = existing.InstallationId, ConnectionToken = token, RegisteredAt = now };
    }
    public async Task<bool> UnregisterAsync(NotificationApplicationEntity app, string installationId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(installationId)) throw new ArgumentException("InstallationId is required.", nameof(installationId));
        var normalizedId = installationId.Trim();
        var existing = await _db.Installations.SingleOrDefaultAsync(
            x => x.ApplicationId == app.Id && x.InstallationId == normalizedId,
            cancellationToken).ConfigureAwait(false);
        if (existing == null) return false;

        if (!string.IsNullOrWhiteSpace(existing.PushEndpoint))
        {
            await _push.SyncTopicsAsync(
                app.Key,
                new PushEndpoint
                {
                    InstallationDatabaseId = existing.Id,
                    InstallationId = existing.InstallationId,
                    Endpoint = existing.PushEndpoint,
                    Kind = existing.PushEndpointKind
                },
                Array.Empty<string>(),
                NotificationSerialization.Topics(existing.TopicsJson).ToArray(),
                cancellationToken).ConfigureAwait(false);
        }

        _db.Installations.Remove(existing);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

}
