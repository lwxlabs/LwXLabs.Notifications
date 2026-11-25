param(
    [string] $DataSource,
    [string] $PackageDirectory
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $PackageDirectory) { $PackageDirectory = Join-Path $root 'artifacts/public' }
$PackageDirectory = [IO.Path]::GetFullPath($PackageDirectory)
$work = Join-Path $root ('artifacts/public-build/' + [guid]::NewGuid().ToString('N'))
$feed = Join-Path $work 'feed'
$cache = Join-Path $work 'packages'
New-Item -ItemType Directory -Force -Path $feed, $cache, $PackageDirectory | Out-Null
$config = Join-Path $work 'NuGet.config'

function Invoke-DotNet([Parameter(ValueFromRemainingArguments = $true)][string[]] $Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments -join ' ') failed ($LASTEXITCODE)." }
}

$projects = @(
    'src/LwXLabs.Notifications.Abstractions/LwXLabs.Notifications.Abstractions.csproj',
    'src/LwXLabs.Notifications/LwXLabs.Notifications.csproj',
    'src/LwXLabs.Notifications.SignalR.Client/LwXLabs.Notifications.SignalR.Client.csproj',
    'src/LwXLabs.Notifications.AspNetCore/LwXLabs.Notifications.AspNetCore.csproj',
    'src/LwXLabs.Notifications.Firebase/LwXLabs.Notifications.Firebase.csproj',
    'src/LwXLabs.Notifications.Firebase.Android/LwXLabs.Notifications.Firebase.Android.csproj'
)

$solution = 'LwXLabs.Notifications.sln'
$tests = 'tests/LwXLabs.Notifications.Tests/LwXLabs.Notifications.Tests.csproj'

# Use only NuGet.org and packages built locally from the Data source.
$escapedFeed = [Security.SecurityElement]::Escape($feed)
@"
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="local-data" value="$escapedFeed" />
  </packageSources>
</configuration>
"@ | Set-Content -LiteralPath $config -Encoding utf8

if ($DataSource) {
    $DataSource = (Resolve-Path -LiteralPath $DataSource).Path
    $version = ([xml](Get-Content (Join-Path $DataSource 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
    if ($version -ne '0.1.0-preview.3') {
        throw "Expected LwXLab.Data version 0.1.0-preview.3; found $version."
    }

    Push-Location $DataSource
    try {
        $data = 'src/LwXLab.Data/LwXLab.Data.csproj'
        $ef = 'src/LwXLab.Data.EntityFrameworkCore/LwXLab.Data.EntityFrameworkCore.csproj'
        foreach ($project in @($data, $ef)) {
            Invoke-DotNet restore $project --configfile $config --packages $cache
            Invoke-DotNet build $project -c Release --no-restore
            Invoke-DotNet pack $project -c Release --no-build --no-restore --output $feed
        }
    }
    finally { Pop-Location }
}

Push-Location $root
try {
    Invoke-DotNet restore $solution --configfile $config --packages $cache
    Invoke-DotNet build $solution -c Release --no-restore
    Invoke-DotNet test --project $tests --configuration Release --no-build --no-restore

    foreach ($project in $projects) {
        Invoke-DotNet pack $project -c Release --no-build --no-restore --output $PackageDirectory
    }
}
finally { Pop-Location }

Write-Host "Packages: $PackageDirectory"
