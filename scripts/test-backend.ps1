[CmdletBinding()]
param(
    [string]$Filter,
    [switch]$NoBuild,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [string]$ResultsDirectory
)
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
$taskRedis = "rednote-backend-test-redis-$taskSuffix"
$taskGorse = "rednote-backend-test-gorse-$taskSuffix"
$taskNetwork = "rednote-backend-test-$taskSuffix"
$taskOldGorse = $env:REDNOTE_TEST_GORSE
$taskOldRedis = $env:REDNOTE_TEST_REDIS
$taskPassword = [Guid]::NewGuid().ToString('N') + '!@{}()+:'
$taskOldPostgres = $env:REDNOTE_TEST_POSTGRES
$taskOldSearch = $env:REDNOTE_TEST_OPENSEARCH
try {
    & $taskDocker network create $taskNetwork | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Unable to create isolated test network.' }
    & $taskDocker build -q -t rednote-gorse:0.5.11 (Join-Path $taskRoot 'infrastructure/gorse') | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Unable to build Gorse test image.' }
    & $taskDocker run --rm -d --name $taskPostgres --network $taskNetwork --label rednote.backend-test=true `
        -p '127.0.0.1::5432' -e "POSTGRES_PASSWORD=$taskPassword" -e POSTGRES_DB=backendtest postgres:18.3 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Unable to start isolated PostgreSQL.' }
    & $taskDocker run --rm -d --name $taskSearch --label rednote.backend-test=true `
        -p '127.0.0.1::9200' -e discovery.type=single-node -e DISABLE_SECURITY_PLUGIN=true `
        -e DISABLE_INSTALL_DEMO_CONFIG=true -e 'OPENSEARCH_JAVA_OPTS=-Xms256m -Xmx256m' `
        opensearchproject/opensearch:3.8.0 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Unable to start isolated OpenSearch.' }
    & $taskDocker run -d --name $taskRedis --entrypoint redis-server --network $taskNetwork -p '127.0.0.1::6379' `
        redis:8.6 --requirepass $taskPassword `
        --loadmodule /usr/local/lib/redis/modules/rejson.so --loadmodule /usr/local/lib/redis/modules/redisearch.so `
        --loadmodule /usr/local/lib/redis/modules/redistimeseries.so | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Unable to start test Redis.' }
    $env:REDNOTE_TEST_REDIS = (& $taskDocker port $taskRedis 6379) + ",password=$taskPassword"
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
    foreach ($taskDatabase in @('gorsedb','recommendationtest')) {
        & $taskDocker exec $taskPostgres createdb -U postgres $taskDatabase
        if ($LASTEXITCODE -ne 0) { throw 'Unable to create isolated recommendation databases.' }
    }
    $taskRedisUri = "redis://:$([Uri]::EscapeDataString($taskPassword))@${taskRedis}:6379/0"
    & $taskDocker run -d --name $taskGorse --network $taskNetwork -p '127.0.0.1::8088' `
        -e "PGHOST=$taskPostgres" -e PGPORT=5432 -e PGUSER=postgres -e "PGPASSWORD=$taskPassword" `
        -e "GORSE_CACHE_STORE=$taskRedisUri" `
        -e GORSE_SERVER_API_KEY=rednote-gorse-test-key `
        -e GORSE_DASHBOARD_USER_NAME=admin -e GORSE_DASHBOARD_PASSWORD=rednote-gorse-test-dashboard rednote-gorse:0.5.11 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Unable to start isolated Gorse.' }
    $taskGorsePort = (& $taskDocker port $taskGorse 8088).Split(':')[-1]
    $env:REDNOTE_TEST_GORSE = "http://127.0.0.1:$taskGorsePort"
    $taskGorseReady = $false
    for ($taskAttempt = 0; $taskAttempt -lt 60; $taskAttempt++) {
        try {
            Invoke-RestMethod "$env:REDNOTE_TEST_GORSE/api/health/ready" -TimeoutSec 2 | Out-Null
            $taskGorseReady = $true; break
        } catch { Start-Sleep -Seconds 1 }
    }
    if (-not $taskGorseReady) { & $taskDocker logs $taskGorse; throw 'Gorse did not become ready.' }
    $taskArguments = @('test', (Join-Path $taskRoot 'Test/RedNote.Backend.Tests'), '--configuration', $Configuration, '--logger', 'console;verbosity=normal')
    if ($ResultsDirectory) {
        $taskResults = if ([IO.Path]::IsPathRooted($ResultsDirectory)) { $ResultsDirectory } else { Join-Path $taskRoot $ResultsDirectory }
        $taskArguments += @('--results-directory', $taskResults, '--logger', 'trx;LogFileName=backend.trx')
    }
    if ($NoBuild) { $taskArguments += @("--no-build", "--no-restore") }
    if ($Filter) { $taskArguments += @('--filter', $Filter) }
    & dotnet @taskArguments
    if ($LASTEXITCODE -ne 0) { & $taskDocker logs --tail 100 $taskGorse; throw 'Backend regression tests failed.' }
} finally {
    $env:REDNOTE_TEST_GORSE = $taskOldGorse
    $env:REDNOTE_TEST_REDIS = $taskOldRedis
    $env:REDNOTE_TEST_POSTGRES = $taskOldPostgres
    $env:REDNOTE_TEST_OPENSEARCH = $taskOldSearch
    # These exact, randomly named containers are created only by this invocation.
    foreach ($taskContainer in @($taskGorse, $taskRedis, $taskPostgres, $taskSearch)) {
        & $taskDocker rm -f $taskContainer 2>$null | Out-Null
    }
    & $taskDocker network rm $taskNetwork 2>$null | Out-Null
}
