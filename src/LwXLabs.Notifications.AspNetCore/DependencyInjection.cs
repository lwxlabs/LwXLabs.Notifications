using System;
using System.Linq;
using Flow.EntityFrameworkCore;
using Flow.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace LwXLabs.Notifications.AspNetCore;

public static class NotificationServerServiceCollectionExtensions
{
    public static IServiceCollection AddLwXLabsNotificationServer(
        this IServiceCollection services,
        Action<NotificationServerOptions>? configure = null)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        var serverOptions = services.AddOptions<NotificationServerOptions>();
        if (configure != null) serverOptions.Configure(configure);
        serverOptions
            .Validate(x => x.MaxEnvelopeBytes > 0, "MaxEnvelopeBytes must be positive.")
            .Validate(x => x.MaxComponents > 0, "MaxComponents must be positive.")
            .Validate(x => x.DefaultLifetime > TimeSpan.Zero, "DefaultLifetime must be positive.")
            .Validate(x => x.AckGracePeriod >= TimeSpan.Zero, "AckGracePeriod cannot be negative.")
            .Validate(x => x.CancelledRetention >= TimeSpan.Zero, "CancelledRetention cannot be negative.")
            .Validate(x => x.ExpiredRetention >= TimeSpan.Zero, "ExpiredRetention cannot be negative.")
            .Validate(x => x.FailedRetention >= TimeSpan.Zero, "FailedRetention cannot be negative.")
            .Validate(x => x.FlowSuccessRetention >= TimeSpan.Zero, "FlowSuccessRetention cannot be negative.")
            .Validate(x => x.FlowFailureRetention >= TimeSpan.Zero, "FlowFailureRetention cannot be negative.")
            .Validate(x => x.CleanupInterval > TimeSpan.Zero, "CleanupInterval must be positive.")
            .Validate(x => x.MaxSendsPerMinute > 0, "MaxSendsPerMinute must be positive.")
            .Validate(x => x.MaxRegistrationsPerMinute > 0, "MaxRegistrationsPerMinute must be positive.")
            .ValidateOnStart();

        services.AddSignalR();
        services.AddFlowEntityFramework<NotificationDbContext>();
        services.AddFlowMessaging()
            .AddCommand<DeliverNotificationCommand, DeliverNotificationHandler>()
            .AddCommand<FinalizeAutoDeliveryCommand, FinalizeAutoDeliveryHandler>()
            .AddOutboxDispatcher(options =>
            {
                options.PollInterval = TimeSpan.FromSeconds(5);
                options.LeaseDuration = TimeSpan.FromMinutes(1);
                options.MaximumRetryDelay = TimeSpan.FromMinutes(2);
                options.MaximumAttempts = 5;
            });

        // Replace Flow's fixed-interval hosted loop with a wakeable loop. The same
        // dispatcher still owns leasing and retry semantics.
        var flowWorker = services.FirstOrDefault(x => x.ServiceType == typeof(IHostedService) && x.ImplementationType == typeof(OutboxDispatcher));
        if (flowWorker != null) services.Remove(flowWorker);
        services.AddSingleton<OutboxDispatcher>();
        services.AddSingleton<NotificationOutboxSignal>();
        services.AddHostedService<NotificationOutboxService>();

        services.TryAddSingleton<IMessageTransport, LocalFlowMessageTransport>();
        services.TryAddScoped<INotificationPushTransport, NoPushNotificationTransport>();
        services.TryAddScoped<INotificationActionSink, NoNotificationActionSink>();
        services.AddScoped<NotificationAccessService>();
        services.AddScoped<NotificationSubmissionService>();
        services.AddSingleton<NotificationApplicationQuota>();
        services.AddScoped<InstallationService>();
        services.AddScoped<PushTargetResolver>();
        services.AddScoped<SignalRNotificationTransport>();
        services.AddHostedService<NotificationCleanupService>();
        return services;
    }
}
