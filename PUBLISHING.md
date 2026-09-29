# Publishing

## 1. Validate locally

Run:

```powershell
.\build\pack.ps1
```

The script restores the package-only solution, builds it, runs the package tests, and packs the five NuGet packages into `artifacts/release`.

Expected packages:

```text
LwXLabs.Notifications.Abstractions.*.nupkg
LwXLabs.Notifications.*.nupkg
LwXLabs.Notifications.SignalR.Client.*.nupkg
LwXLabs.Notifications.AspNetCore.*.nupkg
LwXLabs.Notifications.Firebase.*.nupkg
```

Do not publish if restore, build, tests, or packing fail.

## 2. Private GitHub Packages publishing

Publishing is intentionally manual. After the repository is pushed to GitHub, open Actions and run the `publish-private-packages` workflow.

The workflow:

1. Authenticates the existing private `LwXLab.Data` dependency feed.
2. Runs `build/pack.sh`.
3. Requires exactly five `.nupkg` files.
4. Pushes them to `https://nuget.pkg.github.com/lwxlabs/index.json` with the workflow `GITHUB_TOKEN`.

If `GITHUB_TOKEN` cannot read the private Data packages, either grant `lwxlabs/LwXLabs.Notifications` Actions access to those packages or configure the `LWXLABS_PACKAGES_TOKEN` repository secret with a classic PAT that has `read:packages`.

Package repository metadata is configured for:

```text
https://github.com/lwxlabs/LwXLabs.Notifications
```

After publishing, run the manually dispatched `verify-private-feed` workflow to restore the private packages into an independent .NET 10 application.

Never commit NuGet credentials, GitHub tokens, server API keys, database credentials, Firebase credentials, or generated package artifacts.
