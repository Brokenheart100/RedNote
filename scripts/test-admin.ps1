[CmdletBinding()]
param([switch]$Docker, [string]$GatewayUrl, [string]$FrontendUrl)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskDocker = if (Get-Command docker -ErrorAction SilentlyContinue) { (Get-Command docker).Source } else { Join-Path $env:LOCALAPPDATA 'Programs/DockerDesktop/resources/bin/docker.exe' }
$taskAccounts = @()
$taskEnrollmentFiles = @()
$taskAccountPath = Join-Path $taskRoot 'artifacts/admin-test-accounts.json'
$taskEnvironment = @{}
foreach ($taskName in @('REDNOTE_GATEWAY_URL','REDNOTE_FRONTEND_URL','REDNOTE_ADMIN_TEST_REDIS','REDNOTE_ADMIN_TEST_REDIS_CONTAINER','REDNOTE_ADMIN_TEST_POSTGRES_CONTAINER','REDNOTE_TEST_DOCKER','REDNOTE_ADMIN_TEST_ACCOUNTS','NODE_EXTRA_CA_CERTS')) {
    $taskEnvironment[$taskName] = [Environment]::GetEnvironmentVariable($taskName)
}
Push-Location $taskRoot
try {
    if ($Docker) {
        $env:NODE_EXTRA_CA_CERTS=$null
        $taskComposePath = (Join-Path $taskRoot 'RedNote.AppHost/aspire-output/docker-compose.yaml').Replace('/', '\')
        $taskProject = & $taskDocker compose ls --format json | ConvertFrom-Json | Where-Object ConfigFiles -eq $taskComposePath | Select-Object -First 1
        if (-not $taskProject) { throw 'Deploy the Docker application first.' }
        $taskComposeArgs = @('compose','--project-name',$taskProject.Name,'--env-file',(Join-Path $taskRoot 'RedNote.AppHost/aspire-output/.env.Production'),'-f',$taskComposePath)
        $taskIdentityId = & $taskDocker @taskComposeArgs ps -q identity-service
        $taskPgId = & $taskDocker @taskComposeArgs ps -q postgres
        $taskAdminId = & $taskDocker @taskComposeArgs ps -q admin
        $env:REDNOTE_ADMIN_TEST_REDIS_CONTAINER = & $taskDocker @taskComposeArgs ps -q redis
        $taskAdminInfo = (& $taskDocker inspect $taskAdminId | ConvertFrom-Json)[0]
        $taskAdminEnv = @{}
        foreach ($taskVariable in $taskAdminInfo.Config.Env) { $taskPair=$taskVariable.Split('=',2); $taskAdminEnv[$taskPair[0]]=$taskPair[1] }
        $env:REDNOTE_ADMIN_TEST_REDIS = $taskAdminEnv.REDIS_URI
        if (-not $GatewayUrl) { $GatewayUrl='http://localhost:8080' }
        if (-not $FrontendUrl) { $FrontendUrl='http://localhost:3000' }
    } else {
        $env:REDNOTE_ADMIN_TEST_REDIS_CONTAINER=$null
        & aspire describe --apphost RedNote.AppHost/RedNote.AppHost.csproj --format Json --non-interactive > artifacts/admin-test-resources.json
        if ($LASTEXITCODE -ne 0) { throw 'Start Aspire before running admin tests.' }
        $taskSnapshot=Get-Content artifacts/admin-test-resources.json -Raw|ConvertFrom-Json
        $taskAdmin=$taskSnapshot.resources|Where-Object {$_.name -match '^admin-[a-z]{8}$'}|Select-Object -First 1
        $taskPg=$taskSnapshot.resources|Where-Object name -like 'postgres-*'|Select-Object -First 1
        if ($taskAdmin.state -ne 'Running') { throw 'Wait for the admin resource to start.' }
        $taskPgId=$taskPg.properties.'container.id'
        $env:REDNOTE_ADMIN_TEST_REDIS=$taskAdmin.environment.REDIS_URI
        $env:NODE_EXTRA_CA_CERTS=$taskAdmin.environment.NODE_EXTRA_CA_CERTS
        if (-not $GatewayUrl) { $GatewayUrl='https://localhost:8443' }
        if (-not $FrontendUrl) { $FrontendUrl=$GatewayUrl }
    }
    $env:REDNOTE_GATEWAY_URL=$GatewayUrl; $env:REDNOTE_FRONTEND_URL=$FrontendUrl
    $env:REDNOTE_ADMIN_TEST_POSTGRES_CONTAINER=$taskPgId; $env:REDNOTE_TEST_DOCKER=$taskDocker
    $env:REDNOTE_ADMIN_TEST_ACCOUNTS=$taskAccountPath
    foreach ($taskRoles in @(@('ContentModerator','UserAdministrator','AuditReader'),@('ContentModerator'),@('UserAdministrator'))) {
        $taskEmail="admin_test_$([Guid]::NewGuid().ToString('N'))@example.com"
        $taskPassword="AdminTest@$([Guid]::NewGuid().ToString('N'))Aa1"
        $taskProvision=@{Email=$taskEmail;Roles=$taskRoles;Password=(ConvertTo-SecureString $taskPassword -AsPlainText -Force)}
        if ($Docker) { $taskProvision.DockerContainer=$taskIdentityId }
        & (Join-Path $PSScriptRoot 'provision-admin.ps1') @taskProvision
        $taskFile=Get-ChildItem artifacts/admin-enrollment-*.json|Sort-Object LastWriteTime -Descending|Select-Object -First 1
        $taskEnrollment=Get-Content $taskFile.FullName -Raw|ConvertFrom-Json
        if ($taskEnrollment.Email -ne $taskEmail) { throw 'Unexpected enrollment file.' }
        $taskEnrollmentFiles+=$taskFile.FullName
        $taskAccounts+=@{email=$taskEmail;password=$taskPassword;id=$taskEnrollment.Id;roles=$taskRoles;authenticatorUri=$taskEnrollment.AuthenticatorUri}
    }
    $taskAccounts|ConvertTo-Json -Depth 5|Set-Content $taskAccountPath
    if ($IsWindows) { & icacls $taskAccountPath /inheritance:r /grant:r "$([System.Security.Principal.WindowsIdentity]::GetCurrent().Name):(F)" | Out-Null }
    Push-Location Red-Book
    try {
        & npx playwright test --config playwright.admin.config.ts
        if ($LASTEXITCODE -ne 0) { throw 'Admin end-to-end tests failed.' }
    } finally { Pop-Location }
} finally {
    foreach ($taskAccount in $taskAccounts) {
        $taskUserId=[Guid]::Parse($taskAccount.id).ToString()
        # Only this invocation's newly provisioned test administrators are removed.
        $taskSql="DELETE FROM ""AspNetUsers"" WHERE ""Id""='$taskUserId' AND ""Email"" LIKE 'admin_test_%'"
        & $taskDocker exec $taskPgId sh -c 'PGPASSWORD="$POSTGRES_PASSWORD" exec psql -U "$POSTGRES_USER" -d identitydb -v ON_ERROR_STOP=1 -c "$1"' test-admin $taskSql | Out-Null
    }
    foreach ($taskFile in $taskEnrollmentFiles) { if (Test-Path -LiteralPath $taskFile) { Remove-Item -LiteralPath $taskFile -Force } }
    if ($taskAccounts.Count -gt 0 -and (Test-Path -LiteralPath $taskAccountPath)) { Remove-Item -LiteralPath $taskAccountPath -Force }
    foreach ($taskEntry in $taskEnvironment.GetEnumerator()) { [Environment]::SetEnvironmentVariable($taskEntry.Key,$taskEntry.Value) }
    Pop-Location
}
