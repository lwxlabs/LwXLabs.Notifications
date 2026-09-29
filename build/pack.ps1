$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root "artifacts\release"
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null

function Invoke-DotNet([Parameter(ValueFromRemainingArguments = $true)][string[]] $Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE." }
}

$projects = @(
  "src\LwXLabs.Notifications.Abstractions\LwXLabs.Notifications.Abstractions.csproj",
  "src\LwXLabs.Notifications\LwXLabs.Notifications.csproj",
  "src\LwXLabs.Notifications.SignalR.Client\LwXLabs.Notifications.SignalR.Client.csproj",
  "src\LwXLabs.Notifications.AspNetCore\LwXLabs.Notifications.AspNetCore.csproj",
  "src\LwXLabs.Notifications.Firebase\LwXLabs.Notifications.Firebase.csproj"
)

Push-Location $root
try {
    Invoke-DotNet restore "LwXLabs.Notifications.sln"
    Invoke-DotNet build "LwXLabs.Notifications.sln" -c Release --no-restore
    Invoke-DotNet test "tests\LwXLabs.Notifications.Tests\LwXLabs.Notifications.Tests.csproj" -c Release --no-build --no-restore

    foreach ($project in $projects) {
        Invoke-DotNet pack $project -c Release --no-build --no-restore --output $artifacts
    }
}
finally {
    Pop-Location
}

Write-Host "Packages: $artifacts"
