using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LwXLabs.Notifications.Firebase.Android;

public enum FirebaseAndroidRegistrationChangeKind { Obtained, Refreshed, Confirmed }

public sealed record FirebaseAndroidRegistrationChange(string InstallationId, FirebaseAndroidRegistrationChangeKind Kind);

public sealed record FirebaseAndroidMessage(
    string? MessageId,
    string? Sender,
    string? CollapseKey,
    long SentTimeMilliseconds,
    int TimeToLiveSeconds,
    string? Title,
    string? Body,
    IReadOnlyDictionary<string, string> Data);

/// <summary>Implement in the consuming app and register with DI. Calls may repeat after a failure; deduplicate by message ID where needed.</summary>
public interface IFirebaseAndroidNotificationHandler
{
    Task OnRegistrationAsync(FirebaseAndroidRegistrationChange change, CancellationToken cancellationToken = default);
    Task OnMessageAsync(FirebaseAndroidMessage message, CancellationToken cancellationToken = default);
}

public interface IFirebaseAndroidNotifications
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<string> GetInstallationIdAsync(CancellationToken cancellationToken = default);
}
