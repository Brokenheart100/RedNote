[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskProject = Join-Path $taskRoot 'RedNote.AppHost/RedNote.AppHost.csproj'
[xml]$taskXml = Get-Content -LiteralPath $taskProject
$taskId = $taskXml.Project.PropertyGroup.UserSecretsId | Where-Object { $_ } | Select-Object -First 1
$taskSecretRoot = if ($IsWindows) { Join-Path $env:APPDATA 'Microsoft/UserSecrets' } else { Join-Path ([Environment]::GetFolderPath('UserProfile')) '.microsoft/usersecrets' }
$taskStore = Join-Path $taskSecretRoot "$taskId/secrets.json"
$taskExisting = if (Test-Path -LiteralPath $taskStore) { Get-Content -LiteralPath $taskStore -Raw | ConvertFrom-Json -AsHashtable } else { @{} }
$taskSecretBindings = @{
    'Parameters:admin-oidc-secret' = 'REDNOTE_ADMIN_OIDC_SECRET'
    'Parameters:admin-session-password' = 'REDNOTE_ADMIN_SESSION_PASSWORD'
    'Parameters:gorse-api-key' = 'REDNOTE_GORSE_API_KEY'
    'Parameters:gorse-dashboard-password' = 'REDNOTE_GORSE_DASHBOARD_PASSWORD'
}
foreach ($taskKey in $taskSecretBindings.Keys) {
    # Native parameter variables take precedence. GitHub secret names cannot contain hyphens.
    $taskEnvironmentKey = $taskKey.Replace(':', '__')
    if ([Environment]::GetEnvironmentVariable($taskEnvironmentKey)) { continue }
    $taskProvided = [Environment]::GetEnvironmentVariable($taskSecretBindings[$taskKey])
    if ($taskProvided -or -not $taskExisting.ContainsKey($taskKey)) {
        $taskValue = if ($taskProvided) { $taskProvided } else { [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(48)) }
        & dotnet user-secrets set $taskKey $taskValue --project $taskProject | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Unable to save local service secret.' }
    }
}
Write-Host 'Admin and Gorse secrets are configured in the local AppHost secret store.'
