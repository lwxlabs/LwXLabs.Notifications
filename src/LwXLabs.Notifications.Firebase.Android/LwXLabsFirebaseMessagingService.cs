using System;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Util;
using Firebase.Messaging;

namespace LwXLabs.Notifications.Firebase.Android;

public static class FirebaseAndroidBroadcasts
{
    // Package-scoped explicit broadcast. Applications can opt in to very short,
    // local-only notification rendering before their full DI/network stack is ready.
    public const string MessageReceivedAction =
        "com.lwxlabs.notifications.firebase.MESSAGE_RECEIVED";
}

[Service(Exported = false)]
[IntentFilter(new[] { "com.google.firebase.MESSAGING_EVENT" })]
public sealed class LwXLabsFirebaseMessagingService : FirebaseMessagingService
{
    private static FirebaseAndroidPipeline? _pipeline;

    internal static void Attach(FirebaseAndroidPipeline pipeline) => _pipeline = pipeline;

    public override void OnRegistered(string installationId)
    {
        base.OnRegistered(installationId);
        Persist(store => store.RecordInstallationId(installationId, confirmCurrent: true));
    }

    public override void OnMessageReceived(RemoteMessage message)
    {
        base.OnMessageReceived(message);

        // Give the application a process-local, explicit wakeup before the full
        // Firebase pipeline/DI graph is available. This lets a data-only FCM
        // message render a conversation/bubble notification even after a cold
        // background process start. No network work belongs in this receiver.
        var broadcast = new Intent(FirebaseAndroidBroadcasts.MessageReceivedAction);
        broadcast.SetPackage(PackageName);
        if (!string.IsNullOrWhiteSpace(message.MessageId))
            broadcast.PutExtra("lwx_message_id", message.MessageId);
        foreach (var pair in message.Data)
            broadcast.PutExtra(pair.Key, pair.Value);
        SendBroadcast(broadcast);

        var normalized = FirebaseAndroidMessageNormalizer.Normalize(
            message.MessageId, message.From, message.CollapseKey, message.SentTime,
            message.Ttl, message.GetNotification()?.Title, message.GetNotification()?.Body, message.Data);
        Persist(store => store.RecordMessage(normalized));
    }

    private void Persist(Action<IFirebaseAndroidEventStore> write)
    {
        try
        {
            var storage = new AndroidFirebaseJournalStorage(ApplicationContext ?? throw new InvalidOperationException("Application context is unavailable."));
            write(new FirebaseAndroidEventJournal(storage,
                warning => Log.Warn("LwXLabs.Notifications", warning)));
            if (_pipeline is { } pipeline)
                _ = Task.Run(async () =>
                {
                    try { await pipeline.DrainAsync().ConfigureAwait(false); }
                    catch (Exception ex) { Log.Error("LwXLabs.Notifications", ex.ToString()); }
                });
        }
        catch (Exception ex) { Log.Error("LwXLabs.Notifications", ex.ToString()); }
    }
}
