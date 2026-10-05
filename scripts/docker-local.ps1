[CmdletBinding()]
param(
    [ValidateSet('ps', 'logs', 'stop', 'start', 'restart')][string]$Action = 'ps',
    [string]$Service
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    $env:PATH = "$(Join-Path $env:LOCALAPPDATA 'Programs/DockerDesktop/resources/bin');$env:PATH"
}
$taskComposeFile = (Join-Path $taskRoot 'RedNote.AppHost/aspire-output/docker-compose.yaml').Replace('/', '\')
$taskProjects = & docker compose ls --all --format json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Unable to query Docker Compose.' }
$taskProject = $taskProjects | Where-Object { $_.ConfigFiles -eq $taskComposeFile } | Select-Object -First 1
if (-not $taskProject) { throw 'Run ./scripts/start-docker.ps1 first.' }
$taskArgs = @('compose', '--project-name', $taskProject.Name,
    '--env-file', (Join-Path $taskRoot 'RedNote.AppHost/aspire-output/.env.Production'), '-f', $taskComposeFile)
switch ($Action) {
    'ps' { $taskArgs += @('ps', '-a') }
    'logs' { $taskArgs += @('logs', '--tail', '100', '--follow') }
    'stop' { $taskArgs += 'stop' }
    'start' { $taskArgs += @('up', '-d') }
    'restart' { $taskArgs += 'restart' }
}
if ($Service) { $taskArgs += $Service }
& docker @taskArgs
if ($LASTEXITCODE -ne 0) { throw 'Docker Compose command failed.' }
