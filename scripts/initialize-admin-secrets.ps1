[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskProject = Join-Path $taskRoot 'RedNote.AppHost/RedNote.AppHost.csproj'
[xml]$taskXml = Get-Content -LiteralPath $taskProject
$taskId = $taskXml.Project.PropertyGroup.UserSecretsId | Where-Object { $_ } | Select-Object -First 1
$taskStore = Join-Path $env:APPDATA "Microsoft/UserSecrets/$taskId/secrets.json"
$taskExisting = if (Test-Path -LiteralPath $taskStore) { Get-Content -LiteralPath $taskStore -Raw | ConvertFrom-Json -AsHashtable } else { @{} }
foreach ($taskKey in @('Parameters:admin-oidc-secret', 'Parameters:admin-session-password', 'Parameters:gorse-api-key', 'Parameters:gorse-dashboard-password')) {
    if (-not $taskExisting.ContainsKey($taskKey)) {
        $taskValue = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
        & dotnet user-secrets set $taskKey $taskValue --project $taskProject | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Unable to save local service secret.' }
    }
}
Write-Host 'Admin and Gorse secrets are configured in the local AppHost secret store.'
