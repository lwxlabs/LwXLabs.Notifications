# Implementation status

## Implemented in this preview

- Multi-application central server.
- Per-application sender API keys stored only as SHA-256 hashes.
- Separate per-installation receiver tokens; receiver devices do not need sender API keys.
- User, installation and topic targets.
- Text notifications and versioned JSON component payloads.
- Built-in helpers for progress, status, metric and action-list components.
- Configurable component/envelope size guard.
- `Auto`, `Live`, `Push` and `All` delivery policies.
- SignalR hub delivery using per-installation receiver tokens (header-first; browser-compatible query fallback).
- ACK tracking and durable delayed push fallback for `Auto` mode.
- Firebase Cloud Messaging transport with per-app Firebase project configuration.
- Android Firebase Cloud Messaging receive/bootstrap package using Installation ID registration, background service callbacks, durable callback replay, and notification permission helper.
- Firebase Installation ID support plus legacy registration-token compatibility.
- Topic synchronization for registered installations plus explicit installation unregister/cleanup.
- Scheduled notifications, repeats, max deliveries, expiration and cancellation.
- Dedupe keys with a database unique constraint to protect against concurrent duplicate submissions.
- Flow Messaging outbox/inbox with retries and dead lettering.
- Minimal notification tables; completed notification content is retained only until its configured expiry so delayed push fetches/actions remain valid, then removed.
- Flow inbox/outbox cleanup.
- Provider-neutral ASP.NET Core server package using `LwXLab.Data.EntityFrameworkCore` 0.1.0-preview.3 through its NuGet package dependency.
- Deployable host, Supabase configuration, and Docker deployment files are maintained in the separate `LwXLabs.Notifications.Server` repository.

## Deliberately not hard-wired yet

- MAUI platform notification UI adapters.
- iOS Firebase receive/bootstrap code.
- Blazor component renderers.
- Distributed SignalR backplane for multi-instance live delivery.
- External webhook transport for component action callbacks.
- Full admin UI.
- OpenTelemetry package wiring.
- Component update/replace/complete APIs (initial component delivery and actions are implemented).
- Component capability/version negotiation; clients currently use the version field plus text fallback.

The action endpoint and `INotificationActionSink` are already present so a later webhook/action package does not require changing the receiver contract.

## Verification state

The package projects consume `LwXLab.Data.EntityFrameworkCore` 0.1.0-preview.3 through NuGet. The Flow package dependency remains version 1.2.8. Firebase Admin package/version and current SignalR package versions were checked against their current source/NuGet/docs.

The Android receiver's non-platform logic has automated tests for DI registration, registration changes, normalization, idempotent startup, callback replay, and failure isolation. The full solution restore/build, tests, and NuGet package creation must pass before publishing. An Android device test with a consuming app's Firebase configuration is still required to confirm actual device delivery and permission behavior.
