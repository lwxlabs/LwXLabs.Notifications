# LwXLabs.Notifications

A reusable notification and small-component delivery platform for .NET applications.

This repository contains the reusable NuGet packages only. The deployable ASP.NET Core host is maintained separately in the `LwXLabs.Notifications.Server` project repository. The server-side packages use `LwXLabs.Flow.Messaging` for durable outbox/inbox delivery and `LwXLab.Data` for provider-neutral EF Core registration.

## Projects

| Project | Purpose |
| --- | --- |
| `LwXLabs.Notifications.Abstractions` | Targets, payloads, components, receipts and installation contracts |
| `LwXLabs.Notifications` | HTTP sender + server-side installation registration client |
| `LwXLabs.Notifications.SignalR.Client` | Live receiver for .NET 8/9/10 clients |
| `LwXLabs.Notifications.AspNetCore` | Server engine, SignalR hub, minimal EF storage and Flow durable delivery |
| `LwXLabs.Notifications.Firebase` | Optional Firebase Cloud Messaging push transport |

## Send a normal notification

```csharp
builder.Services.AddLwXLabsNotifications(options =>
{
    options.ServerUrl = new Uri(configuration["Notifications:Server"]!);
    options.Application = "home";
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

## Server-side package dependencies

`LwXLabs.Notifications.AspNetCore` restores `LwXLab.Data.EntityFrameworkCore` version `0.1.0-preview.3` from the private `lwxlabs` GitHub NuGet feed configured in `NuGet.config`. The source checkout is no longer embedded in this repository.

A GitHub classic PAT with package access is required by NuGet. The helper validates the private feed, restores this package solution, and runs the package tests without writing the token to the repository:

```powershell
.\build\Verify-PrivateDataPackages.ps1
```

## Standalone server

The deployable .NET 10 host, Docker/Compose files, Supabase host configuration, Firebase credentials guidance, and operational documentation now live in the separate `LwXLabs.Notifications.Server` project repository.

`LwXLabs.Notifications.Firebase` remains a reusable package in this repository and uses Firebase Admin .NET `3.7.0`.

## Build and private packages

```powershell
.\build\pack.ps1
```

or

```bash
./build/pack.sh
```

Packages are written to `artifacts/release`. See `PUBLISHING.md` for validation and publishing steps and `PRIVATE_PACKAGES.md` for private GitHub Packages consumption.

See `IMPLEMENTATION_STATUS.md` for what is implemented and what still needs platform-specific work.
