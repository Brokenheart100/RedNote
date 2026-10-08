[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskComposePath = (Join-Path $taskRoot 'RedNote.AppHost/aspire-output/docker-compose.yaml').Replace('/', '\')
$taskProject = docker compose ls --format json | ConvertFrom-Json | Where-Object ConfigFiles -eq $taskComposePath | Select-Object -First 1
if (-not $taskProject) { throw 'Deploy the Docker application before importing historical audit.' }
$taskComposeArgs = @('compose','--project-name',$taskProject.Name,'--env-file',(Join-Path $taskRoot 'RedNote.AppHost/aspire-output/.env.Production'),'-f',$taskComposePath)
$taskPg = docker @taskComposeArgs ps -q postgres
$taskAdmin = docker @taskComposeArgs ps -q admin-service
if (-not $taskPg -or -not $taskAdmin) { throw 'PostgreSQL and AdminService must be running.' }
$taskRecords = [System.Collections.Generic.List[object]]::new()
foreach ($taskSource in @('content','user')) {
    $taskSql = @"
SELECT COALESCE(json_agg(json_build_object('id',"Id",'source','$taskSource','actorUserId',"ActorUserId",
    'action',"Action",'targetType',"TargetType",'targetId',"TargetId",'reason',"Reason",'change',"Change",
    'traceId',"TraceId",'createdAtUtc',"CreatedAtUtc")), '[]'::json)::text FROM "AdminAudit";
"@
    $taskJson = docker exec $taskPg sh -c 'PGPASSWORD="$POSTGRES_PASSWORD" exec psql -U "$POSTGRES_USER" -d "$1" -v ON_ERROR_STOP=1 -At -c "$2"' audit-export "${taskSource}db" $taskSql
    if ($LASTEXITCODE -ne 0) { throw "Unable to read $taskSource audit." }
    foreach ($taskEntry in ($taskJson | ConvertFrom-Json)) { $taskRecords.Add($taskEntry) }
}
$taskBatch = [Guid]::NewGuid().ToString('N')
$taskFile = Join-Path $taskRoot "artifacts/admin-audit-backfill-$taskBatch.json"
$taskContainerFile = "/tmp/admin-audit-backfill-$taskBatch.json"
ConvertTo-Json -InputObject $taskRecords.ToArray() -Depth 8 | Set-Content -LiteralPath $taskFile -Encoding utf8NoBOM
try {
    docker cp $taskFile "${taskAdmin}:$taskContainerFile"
    if ($LASTEXITCODE -ne 0) { throw 'Unable to copy the audit batch.' }
    docker exec $taskAdmin dotnet /app/RedNote.AdminService.dll "--ImportAuditFile=$taskContainerFile"
    if ($LASTEXITCODE -ne 0) { throw 'Audit import failed; source records remain unchanged.' }
    Write-Host "Historical audit imported: $($taskRecords.Count) records. Batch: $taskFile"
} finally {
    docker exec -u 0 $taskAdmin rm -f $taskContainerFile | Out-Null
}
