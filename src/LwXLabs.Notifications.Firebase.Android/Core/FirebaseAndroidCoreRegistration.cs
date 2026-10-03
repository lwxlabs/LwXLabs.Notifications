using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LwXLabs.Notifications.Firebase.Android;

internal static class FirebaseAndroidCoreRegistration
{
    public static IServiceCollection AddCore(IServiceCollection services, Action<Exception> reportError, Action<string> reportWarning)
    {
        services.TryAddSingleton<IFirebaseAndroidEventStore>(sp => new FirebaseAndroidEventJournal(
            sp.GetRequiredService<IFirebaseAndroidJournalStorage>(), reportWarning));
        services.TryAddSingleton(sp => new FirebaseAndroidPipeline(
            sp.GetRequiredService<IFirebaseAndroidPlatform>(),
            sp.GetRequiredService<IFirebaseAndroidEventStore>(),
            sp.GetServices<IFirebaseAndroidNotificationHandler>(), reportError));
        return services;
    }
}
