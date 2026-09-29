# Private preview packages

The `0.1.0-preview.2` notification packages are prepared for the private `lwxlabs` GitHub NuGet feed through the manually dispatched `publish-private-packages` workflow.

Feed: `https://nuget.pkg.github.com/lwxlabs/index.json`

Repository: `https://github.com/lwxlabs/LwXLabs.Notifications`

The workflow publishes exactly these five packages:

- `LwXLabs.Notifications.Abstractions`
- `LwXLabs.Notifications`
- `LwXLabs.Notifications.SignalR.Client`
- `LwXLabs.Notifications.AspNetCore`
- `LwXLabs.Notifications.Firebase`

The repository itself also restores private `LwXLab.Data` packages. In GitHub Actions, either grant this repository Actions access to the required private Data packages or add a repository secret named `LWXLABS_PACKAGES_TOKEN` containing a GitHub personal access token (classic) with `read:packages`. The publishing step uses the workflow's `GITHUB_TOKEN` with `packages: write` for the notification packages.

For a Windows development machine, create a GitHub personal access token **(classic)** with `read:packages` and access to the private packages. Configure the existing user-level NuGet source named `GitHub`:

```powershell
$nugetConfig = Join-Path $env:APPDATA 'NuGet\NuGet.Config'
$secureToken = Read-Host 'GitHub classic PAT (read:packages)' -AsSecureString
$token = [System.Net.NetworkCredential]::new('', $secureToken).Password
try {
  dotnet nuget update source GitHub `
    --source 'https://nuget.pkg.github.com/lwxlabs/index.json' `
    --username lwxlabs --password $token --configfile $nugetConfig
  if ($LASTEXITCODE -ne 0) { throw 'Could not update the GitHub source.' }
} finally {
  $token = $null
  $secureToken.Dispose()
}
```

Confirm the source and search with prereleases enabled:

```powershell
dotnet nuget list source --configfile $nugetConfig
dotnet package search LwXLabs.Notifications --exact-match --prerelease `
  --source 'https://nuget.pkg.github.com/lwxlabs/index.json' `
  --configfile $nugetConfig
```

Install only the packages an application needs, for example:

```powershell
dotnet add package LwXLabs.Notifications --version 0.1.0-preview.2
dotnet add package LwXLabs.Notifications.SignalR.Client --version 0.1.0-preview.2
```

Use a new package version for every subsequent publish. GitHub Packages does not replace an existing package version.
