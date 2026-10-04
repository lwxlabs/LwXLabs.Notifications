using System;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using IOnCompleteListener = global::Android.Gms.Tasks.IOnCompleteListener;
using Firebase;
using Firebase.Installations;
using Firebase.Messaging;

namespace LwXLabs.Notifications.Firebase.Android;

internal sealed class AndroidFirebasePlatform : IFirebaseAndroidPlatform
{
    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FirebaseApp.InitializeApp(Application.Context) == null)
            throw new InvalidOperationException("Firebase configuration is missing. Add the consuming app's google-services.json with GoogleServicesJson build action.");
        return Task.CompletedTask;
    }

    public bool IsAutoRegistrationEnabled => FirebaseMessaging.Instance.AutoInitEnabled;

    public async Task RegisterAsync(CancellationToken cancellationToken)
    {
        await AwaitAsync(FirebaseMessaging.Instance.Register(), cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> GetInstallationIdAsync(CancellationToken cancellationToken)
    {
        var installationId = await AwaitAsync(FirebaseInstallations.Instance.GetId(), cancellationToken).ConfigureAwait(false);
        return !string.IsNullOrWhiteSpace(installationId)
            ? installationId
            : throw new InvalidOperationException("Firebase returned an empty Installation ID.");
    }

    private static Task<string?> AwaitAsync(global::Android.Gms.Tasks.Task task, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        task.AddOnCompleteListener(new FirebaseTaskCompletionListener(completion));
        return completion.Task.WaitAsync(cancellationToken);
    }

    private sealed class FirebaseTaskCompletionListener : Java.Lang.Object, IOnCompleteListener
    {
        private readonly TaskCompletionSource<string?> _completion;
        public FirebaseTaskCompletionListener(TaskCompletionSource<string?> completion) => _completion = completion;

        public void OnComplete(global::Android.Gms.Tasks.Task task)
        {
            if (task.IsSuccessful) _completion.TrySetResult(task.Result?.ToString());
            else _completion.TrySetException(new InvalidOperationException("Firebase registration failed. Check the app's Firebase configuration and installation-ID manifest flag.", task.Exception));
        }
    }
}
