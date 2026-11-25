# LwXLabs.Notifications

A reusable notification and small-component delivery platform for .NET applications.

Reusable .NET notification packages with SignalR, Firebase Cloud Messaging and durable delivery through [Flow](https://github.com/lwxlabs/LwXLab.Flow) and [Data](https://github.com/lwxlabs/LwXLab.Data). The application host is not included.

## Projects

| Project | Purpose |
| --- | --- |
| `LwXLabs.Notifications.Abstractions` | Targets, payloads, components, receipts and installation contracts |
| `LwXLabs.Notifications` | HTTP sender + server-side installation registration client |
| `LwXLabs.Notifications.SignalR.Client` | Live receiver for .NET 8/9/10 clients |
| `LwXLabs.Notifications.AspNetCore` | Server engine, SignalR hub, minimal EF storage and Flow durable delivery |
| `LwXLabs.Notifications.Firebase` | Optional Firebase Cloud Messaging push transport |
| `LwXLabs.Notifications.Firebase.Android` | Android FCM receiver, Firebase Installation ID registration, and notification permission helper |

## Send a normal notification

```csharp
builder.Services.AddLwXLabsNotifications(options =>
{
    options.ServerUrl = new Uri(configuration["Notifications:Server"]!);
    options.Application = "sample";
    options.ApiKey = configuration["Notifications:ApiKey"]!;
});

await sender.SendAsync(
    NotificationRequest.ToUser(userId)
        .WithText("Server offline", "Home server cannot be reached."));
```

## Send small components

```csharp
await sender.SendAsync(
    NotificationRequest.ToUser(userId)
        .WithText("Backup", "Backup is 72% complete")
        .AddComponent(NotificationComponent.Progress("Backup", .72, "Uploading files...")));
```

Custom component payloads remain JSON objects:

```csharp
var stock = NotificationComponent.Create("stock-price", new
{
    symbol = "NVDA",
    price = 184.20m,
    change = 2.4m
});
```


## Receiver registration and security

`RegisterInstallationAsync` is a trusted backend operation because it uses the application's sender API key. Do **not** embed that sender key in MAUI, Blazor, desktop, or browser clients. Registration returns a separate per-installation connection token; receiver endpoints and SignalR use only that token.

```csharp
var registered = await sender.RegisterInstallationAsync(new RegisterInstallationRequest
{
    InstallationId = installationId,
    UserId = userId,
    Platform = "android",
    PushEndpoint = firebaseInstallationId,
    PushEndpointKind = PushEndpointKind.FirebaseInstallationId,
    Topics = { "builds" }
});
```

A .NET receiver can then connect without the sender API key:

```csharp
await using var receiver = new NotificationReceiver(new NotificationReceiverOptions
{
    ServerUrl = new Uri(serverUrl),
    InstallationToken = registered.ConnectionToken
});

receiver.NotificationReceived += envelope =>
{
    // Render envelope.Content text/components in the app.
    return Task.CompletedTask;
};

await receiver.StartAsync();
```

Interactive component actions can be sent through SignalR or the authenticated HTTP fallback with `InvokeActionAsync` / `InvokeActionHttpAsync`.

## Receive Firebase messages in an Android app

Add `LwXLabs.Notifications.Firebase.Android` to a `net10.0-android` app. The app owns its Firebase registration and configuration. Put that app's `google-services.json` in the Android project, not in this package, and mark it with the `GoogleServicesJson` build action:

```xml
<PackageReference Include="LwXLabs.Notifications.Firebase.Android" Version="0.1.0-preview.3" />
<GoogleServicesJson Include="Platforms\Android\google-services.json" />
```

Enable Firebase's current Installation ID registration mode and Android 13+ notification permission in the **consuming app's** `Platforms/Android/AndroidManifest.xml`:

```xml
<manifest xmlns:android="http://schemas.android.com/apk/res/android">
  <uses-permission android:name="android.permission.POST_NOTIFICATIONS" />
  <application>
    <meta-data android:name="firebase_messaging_installation_id_enabled" android:value="true" />
  </application>
</manifest>
```

Register the package and one or more app-owned handlers in DI (for MAUI, use `MauiProgram.CreateMauiApp`; for plain .NET Android, use the app's `IServiceCollection`):

```csharp
builder.Services.AddLwXLabsNotificationsFirebaseAndroid();
builder.Services.AddSingleton<IFirebaseAndroidNotificationHandler, MyFirebaseHandler>();

// Once the app's service provider exists, on startup:
await services.GetRequiredService<IFirebaseAndroidNotifications>().InitializeAsync();
var installationId = await services.GetRequiredService<IFirebaseAndroidNotifications>().GetInstallationIdAsync();
```

`MyFirebaseHandler` implements `OnRegistrationAsync(FirebaseAndroidRegistrationChange, CancellationToken)` and `OnMessageAsync(FirebaseAndroidMessage, CancellationToken)`. Registration changes include `Obtained`, `Refreshed`, and `Confirmed` (the same ID on a later app start). Send `change.InstallationId` to your **trusted backend**, which calls `RegisterInstallationAsync` with `PushEndpointKind.FirebaseInstallationId`. Never put the server application API key in the Android app. Message callbacks expose title, body, message ID, sender, timestamps, and a copied data dictionary; your app decides what to display or open. Callbacks can run on a background thread, so marshal UI updates to the UI thread.

On Android 13+, call `FirebaseAndroidNotificationPermission.RequestIfNeeded(activity, requestCode)` from an Activity at a suitable time and handle its permission result there. The helper returns `true` when permission is already granted. Permission controls visible notifications; the package does not post a notification or create a notification channel. Firebase delivers notification-only messages received in the background to the system tray, so those do not invoke this package's message callback until the app handles the launch intent. Use a data payload when the app must process a background message.

Firebase callbacks received before DI initialization are saved and replayed at startup. Failed consumer callbacks are logged and retained for retry on a later initialization or incoming event. Consumers should handle at-least-once delivery and deduplicate on message ID where needed. The pending message inbox holds up to 100 messages; registration changes are not discarded by that bound.

## Repeat and expiry

```csharp
await sender.SendAsync(
    NotificationRequest.ToUser(userId)
        .WithText("Server unavailable", "Still offline")
        .Repeat(TimeSpan.FromMinutes(15), maximumDeliveries: 4)
        .ExpireAfter(TimeSpan.FromHours(2))
        .DeduplicateBy($"server-offline:{serverId}"));
```

Flow's durable outbox `NotBefore` and TTL are used for scheduling/repeats, so this first version does not require Quartz.

## Delivery modes

- `Auto`: SignalR first, then a durable fallback command checks for an ACK after the configured grace period and uses push only if needed.
- `Live`: SignalR only.
- `Push`: push provider only.
- `All`: SignalR and push.

Every delivery uses the same notification ID + delivery number so receivers can deduplicate an external-provider retry.

## Minimal persistence

Notification-owned tables contain only:

- applications
- installations
- currently active notification items

Completed items remain fetchable/actionable only until their configured `ExpiresAt`, then the cleanup worker removes them. Cancelled/expired rows are removed quickly. Flow's completed inbox/outbox rows are also pruned after short retention, while failed/dead-letter infrastructure rows are retained longer for diagnostics.

## Build from source

Requires the .NET 10 SDK and Android workload.

```bash
dotnet workload install android
git clone https://github.com/lwxlabs/LwXLab.Data.git ../LwXLab.Data
git -C ../LwXLab.Data checkout ec174f06faf957a9f4be3aecacacb9aba2caa9b5
pwsh -File build/pack-public.ps1 -DataSource ../LwXLab.Data
```

The public build uses NuGet.org and builds the required Data packages from source. No private feed credentials are needed. The Data repository must be public for the clone and public CI to work.

Packages are written to `artifacts/public`. The existing private feed workflow is documented in [PRIVATE_PACKAGES.md](PRIVATE_PACKAGES.md).

The Android receiver targets .NET 10 Android. Use your own ASP.NET Core host and Firebase project; production deployment is not included.

See [IMPLEMENTATION_STATUS.md](IMPLEMENTATION_STATUS.md) for platform limitations. Apache-2.0 license.
