param([string] $TokenFile)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$config = Join-Path $env:APPDATA 'NuGet\NuGet.Config'
$feed = 'https://nuget.pkg.github.com/lwxlabs/index.json'

if (-not (Test-Path -LiteralPath $config)) {
    throw "User NuGet configuration not found: $config"
}
[xml]$settings = Get-Content -LiteralPath $config
$source = @($settings.configuration.packageSources.add | Where-Object { $_.value -eq $feed })
if ($source.Count -ne 1 -or $source[0].key -ne 'GitHub') {
    throw 'Expected exactly one user-level source named GitHub for the lwxlabs feed. No configuration was changed.'
}

$previousCredential = [Environment]::GetEnvironmentVariable('NuGetPackageSourceCredentials_GitHub', 'Process')
$secureToken = $null
$token = $null
try {
    if ($TokenFile) {
        $resolvedTokenFile = (Resolve-Path -LiteralPath $TokenFile -ErrorAction Stop).Path
        $token = (Get-Content -LiteralPath $resolvedTokenFile -Raw -ErrorAction Stop).Trim()
    }
    else {
        $secureToken = Read-Host 'GitHub classic PAT with read:packages (input hidden)' -AsSecureString
        $token = [System.Net.NetworkCredential]::new('', $secureToken).Password
    }
    if ([string]::IsNullOrWhiteSpace($token)) { throw 'No token was entered.' }
    try {
        $response = Invoke-WebRequest -Uri 'https://api.github.com/user' -Headers @{
            Authorization = "Bearer $token"
            'User-Agent' = 'LwXLabs.Notifications.PrivatePackageCheck'
        }
    }
    catch { throw 'GitHub rejected the token. Create a valid classic PAT with read:packages and try again.' }
    $scopes = [string]$response.Headers['X-OAuth-Scopes']
    $packageScopes = @($scopes -split ',\s*')
    if (-not ($packageScopes | Where-Object { $_ -in @('read:packages', 'write:packages', 'delete:packages') })) {
        throw 'This GitHub token lacks read:packages. No NuGet credential was changed.'
    }
    $gitHubUser = ($response.Content | ConvertFrom-Json).login
    if ([string]::IsNullOrWhiteSpace($gitHubUser)) { throw 'Could not identify the GitHub token owner.' }

    & dotnet nuget update source GitHub --source $feed --username $gitHubUser --password $token --configfile $config
    if ($LASTEXITCODE -ne 0) { throw 'Could not update the GitHub NuGet source.' }

    # A process-level credential wins over the saved one, including when another shell set a stale value.
    [Environment]::SetEnvironmentVariable('NuGetPackageSourceCredentials_GitHub', "Username=$gitHubUser;Password=$token", 'Process')
    $found = & dotnet package search LwXLab.Data.Supabase --exact-match --prerelease --source $feed --configfile $config
    if ($LASTEXITCODE -ne 0 -or -not ($found | Where-Object { $_ -match '(?i)\blwxlab\.data\.supabase\b' })) {
        throw 'GitHub did not return LwXLab.Data.Supabase. Check token validity and read:packages access.'
    }
    $found | Write-Output

    Push-Location $root
    try {
        & dotnet restore .\LwXLabs.Notifications.sln --force --no-cache --verbosity minimal
        if ($LASTEXITCODE -ne 0) { throw 'Notifications restore from the private feed failed.' }
        & dotnet test .\LwXLabs.Notifications.sln --no-restore --verbosity minimal
        if ($LASTEXITCODE -ne 0) { throw 'Notifications tests failed.' }
    }
    finally { Pop-Location }
    Write-Host 'Private GitHub package search, Notifications restore, and tests passed.'
}
finally {
    [Environment]::SetEnvironmentVariable('NuGetPackageSourceCredentials_GitHub', $previousCredential, 'Process')
    $token = $null
    if ($secureToken) { $secureToken.Dispose() }
}
