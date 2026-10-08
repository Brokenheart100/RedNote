[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
Push-Location $taskRoot
try {
    & (Join-Path $PSScriptRoot 'initialize-admin-secrets.ps1')
    dotnet dev-certs https --check --trust
    if ($LASTEXITCODE -ne 0) {
        throw "Trust the local certificate first: dotnet dev-certs https --trust"
    }
    aspire run --apphost RedNote.AppHost/RedNote.AppHost.csproj --non-interactive
    if ($LASTEXITCODE -ne 0) { throw 'Aspire exited. Check its resource logs for startup errors.' }
}
finally { Pop-Location }
