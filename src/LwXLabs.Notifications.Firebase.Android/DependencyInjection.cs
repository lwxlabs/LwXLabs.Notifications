using System;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LwXLabs.Notifications.Firebase.Android;

public static class FirebaseAndroidServiceCollectionExtensions
{
    public static IServiceCollection AddLwXLabsNotificationsFirebaseAndroid(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IFirebaseAndroidPlatform, AndroidFirebasePlatform>();
        services.TryAddSingleton<IFirebaseAndroidJournalStorage>(_ => new AndroidFirebaseJournalStorage(Application.Context));
        FirebaseAndroidCoreRegistration.AddCore(services,
            ex => global::Android.Util.Log.Error("LwXLabs.Notifications", ex.ToString()),
            warning => global::Android.Util.Log.Warn("LwXLabs.Notifications", warning));
        services.TryAddSingleton<IFirebaseAndroidNotifications, AndroidFirebaseNotifications>();
        return services;
    }
}

internal sealed class AndroidFirebaseNotifications : IFirebaseAndroidNotifications
{
    private readonly FirebaseAndroidPipeline _pipeline;

    public AndroidFirebaseNotifications(FirebaseAndroidPipeline pipeline) => _pipeline = pipeline;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        LwXLabsFirebaseMessagingService.Attach(_pipeline);
        return _pipeline.InitializeAsync(cancellationToken);
    }

    public Task<string> GetInstallationIdAsync(CancellationToken cancellationToken = default)
    {
        LwXLabsFirebaseMessagingService.Attach(_pipeline);
        return _pipeline.GetInstallationIdAsync(cancellationToken);
    }
}
