[CmdletBinding()]
param(
    [string]$GatewayPublicUrl = 'http://localhost:8080',
    [string]$FrontendPublicUrl = 'http://localhost:3000',
    [string]$MediaPublicUrl = 'http://localhost:9000'
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskDocker = Get-Command docker -ErrorAction SilentlyContinue
if (-not $taskDocker) {
    $taskDockerPath = Join-Path $env:LOCALAPPDATA 'Programs/DockerDesktop/resources/bin'
    if (-not (Test-Path (Join-Path $taskDockerPath 'docker.exe'))) { throw 'Docker CLI not found.' }
    $env:PATH = "$taskDockerPath;$env:PATH"
}
Push-Location $taskRoot
try {
    & docker info --format '{{.ServerVersion}}'
    if ($LASTEXITCODE -ne 0) { throw 'Start Docker Desktop first.' }
    & aspire deploy --apphost RedNote.AppHost/RedNote.AppHost.csproj `
        -o RedNote.AppHost/aspire-output --non-interactive -- `
        --LocalDocker=true "--Parameters:gateway-public-url=$GatewayPublicUrl" `
        "--Parameters:frontend-public-url=$FrontendPublicUrl" "--Parameters:media-public-url=$MediaPublicUrl"
    if ($LASTEXITCODE -ne 0) { throw 'Docker deployment failed. See the Aspire pipeline output.' }
    $taskComposeFile = Join-Path $taskRoot 'RedNote.AppHost/aspire-output/docker-compose.yaml'
    $taskComposeProjects = & docker compose ls --format json | ConvertFrom-Json
    $taskComposeProject = $taskComposeProjects | Where-Object { $_.ConfigFiles -eq $taskComposeFile.Replace('/', '\') } | Select-Object -First 1
    if (-not $taskComposeProject) { throw 'Aspire Compose project was not found.' }
    & docker compose --project-name $taskComposeProject.Name --env-file RedNote.AppHost/aspire-output/.env.Production `
        -f $taskComposeFile ps -a
    if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect Docker services.' }
} finally { Pop-Location }
