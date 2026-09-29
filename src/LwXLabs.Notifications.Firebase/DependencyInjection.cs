using System;
using LwXLabs.Notifications.AspNetCore;
using Microsoft.Extensions.DependencyInjection;

namespace LwXLabs.Notifications.Firebase;

public static class FirebaseNotificationServiceCollectionExtensions
{
    public static IServiceCollection AddLwXLabsNotificationsFirebase(this IServiceCollection services, Action<FirebaseNotificationsOptions> configure)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        if (configure == null) throw new ArgumentNullException(nameof(configure));
        services.AddOptions<FirebaseNotificationsOptions>()
            .Configure(configure)
            .Validate(x => x.InlinePayloadByteLimit > 0, "InlinePayloadByteLimit must be positive.")
            .Validate(x => x.Applications != null, "Applications cannot be null.")
            .ValidateOnStart();
        services.AddSingleton<INotificationPushTransport, FirebasePushTransport>();
        return services;
    }
}
