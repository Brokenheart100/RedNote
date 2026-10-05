[CmdletBinding()]
param([string]$Filter)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskDocker = 'docker'
if ($IsWindows) {
    $taskDockerPath = Join-Path $env:LOCALAPPDATA 'Programs/DockerDesktop/resources/bin/docker.exe'
    if (Test-Path -LiteralPath $taskDockerPath) { $taskDocker = $taskDockerPath }
}
$taskSuffix = [Guid]::NewGuid().ToString('N').Substring(0, 12)
$taskPostgres = "rednote-backend-test-pg-$taskSuffix"
$taskSearch = "rednote-backend-test-search-$taskSuffix"
$taskPassword = [Guid]::NewGuid().ToString('N')
$taskOldPostgres = $env:REDNOTE_TEST_POSTGRES
$taskOldSearch = $env:REDNOTE_TEST_OPENSEARCH
try {
    & $taskDocker run --rm -d --name $taskPostgres --label rednote.backend-test=true `
        -p '127.0.0.1::5432' -e "POSTGRES_PASSWORD=$taskPassword" -e POSTGRES_DB=backendtest postgres:18.3 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Unable to start isolated PostgreSQL.' }
    & $taskDocker run --rm -d --name $taskSearch --label rednote.backend-test=true `
        -p '127.0.0.1::9200' -e discovery.type=single-node -e DISABLE_SECURITY_PLUGIN=true `
        -e DISABLE_INSTALL_DEMO_CONFIG=true -e 'OPENSEARCH_JAVA_OPTS=-Xms256m -Xmx256m' `
        opensearchproject/opensearch:3.8.0 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Unable to start isolated OpenSearch.' }
    $taskPgPort = (& $taskDocker port $taskPostgres 5432).Split(':')[-1]
    $taskSearchPort = (& $taskDocker port $taskSearch 9200).Split(':')[-1]
    $env:REDNOTE_TEST_POSTGRES = "Host=127.0.0.1;Port=$taskPgPort;Database=backendtest;Username=postgres;Password=$taskPassword"
    $env:REDNOTE_TEST_OPENSEARCH = "http://127.0.0.1:$taskSearchPort"
    $taskReady = $false
    for ($taskAttempt = 0; $taskAttempt -lt 60; $taskAttempt++) {
        try {
            Invoke-RestMethod "$env:REDNOTE_TEST_OPENSEARCH/_cluster/health" -TimeoutSec 2 | Out-Null
            & $taskDocker exec $taskPostgres pg_isready -U postgres *> $null
            if ($LASTEXITCODE -eq 0) { $taskReady = $true; break }
        } catch { }
        Start-Sleep -Seconds 1
    }
    if (-not $taskReady) { throw 'Test infrastructure did not become ready within 60 attempts.' }
    $taskArguments = @('test', (Join-Path $taskRoot 'Test/RedNote.Backend.Tests'), '--logger', 'console;verbosity=normal')
    if ($Filter) { $taskArguments += @('--filter', $Filter) }
    & dotnet @taskArguments
    if ($LASTEXITCODE -ne 0) { throw 'Backend regression tests failed.' }
} finally {
    $env:REDNOTE_TEST_POSTGRES = $taskOldPostgres
    $env:REDNOTE_TEST_OPENSEARCH = $taskOldSearch
    # These exact, randomly named containers are created only by this invocation.
    foreach ($taskContainer in @($taskPostgres, $taskSearch)) {
        & $taskDocker rm -f $taskContainer 2>$null | Out-Null
    }
}
