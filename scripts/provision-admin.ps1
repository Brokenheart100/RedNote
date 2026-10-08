[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Email,
    [Parameter(Mandatory)][ValidateSet('ContentModerator', 'UserAdministrator', 'AuditReader')][string[]]$Roles,
    [SecureString]$Password,
    [string]$DockerContainer
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
New-Item -ItemType Directory -Path (Join-Path $taskRoot 'artifacts') -Force | Out-Null
if (-not $Password) { $Password = Read-Host 'New administrator password' -AsSecureString }
$taskOutput = Join-Path $taskRoot "artifacts/admin-enrollment-$([Guid]::NewGuid().ToString('N')).json"
$taskCredentials = [System.Net.NetworkCredential]::new('', $Password)
$taskProcess = [System.Diagnostics.ProcessStartInfo]::new()
$taskProcess.UseShellExecute = $false
$taskProcess.WorkingDirectory = $taskRoot
$taskBootstrap = @{
    AdminBootstrap__Email          = $Email
    AdminBootstrap__Password       = $taskCredentials.Password
    AdminBootstrap__Roles          = ($Roles -join ',')
    AdminBootstrap__EnrollmentFile = $taskOutput
}
try {
    if ($DockerContainer) {
        $taskDocker = Get-Command docker -ErrorAction SilentlyContinue
        $taskProcess.FileName = if ($taskDocker) { $taskDocker.Source } else { Join-Path $env:LOCALAPPDATA 'Programs/DockerDesktop/resources/bin/docker.exe' }
        $taskBootstrap.AdminBootstrap__EnrollmentFile = '/tmp/rednote-admin-enrollment.json'
        foreach ($taskArgument in @('exec')) { $taskProcess.ArgumentList.Add($taskArgument) }
        foreach ($taskEntry in $taskBootstrap.GetEnumerator()) {
            $taskProcess.Environment[$taskEntry.Key] = $taskEntry.Value
            $taskProcess.ArgumentList.Add('--env'); $taskProcess.ArgumentList.Add($taskEntry.Key)
        }
        foreach ($taskArgument in @($DockerContainer, 'dotnet', 'RedNote.IdentityService.dll', '--provision-admin')) { $taskProcess.ArgumentList.Add($taskArgument) }
    }
    else {
        & dotnet build (Join-Path $taskRoot 'RedNote.IdentityService') -c Release --nologo --verbosity quiet
        if ($LASTEXITCODE -ne 0) { throw 'Unable to build IdentityService.' }
        $taskSnapshotPath = Join-Path $taskRoot 'artifacts/admin-provision-resources.json'
        & aspire describe --apphost (Join-Path $taskRoot 'RedNote.AppHost/RedNote.AppHost.csproj') --format Json --non-interactive > $taskSnapshotPath
        if ($LASTEXITCODE -ne 0) { throw 'Start the local Aspire application first.' }
        $taskSnapshot = Get-Content $taskSnapshotPath -Raw | ConvertFrom-Json
        $taskIdentity = $taskSnapshot.resources | Where-Object name -like 'identity-service-*' | Select-Object -First 1
        if (-not $taskIdentity -or $taskIdentity.state -ne 'Running') { throw 'IdentityService must be running with completed database migrations.' }
        $taskProcess.FileName = (Get-Command dotnet).Source
        foreach ($taskEntry in $taskIdentity.environment.PSObject.Properties) { $taskProcess.Environment[$taskEntry.Name] = [string]$taskEntry.Value }
        foreach ($taskEntry in $taskBootstrap.GetEnumerator()) { $taskProcess.Environment[$taskEntry.Key] = $taskEntry.Value }
        $taskDll = Get-ChildItem (Join-Path $taskRoot 'RedNote.IdentityService/bin/Release') -Filter RedNote.IdentityService.dll -Recurse | Where-Object FullName -notmatch '/ref/' | Select-Object -First 1
        $taskProcess.ArgumentList.Add($taskDll.FullName); $taskProcess.ArgumentList.Add('--provision-admin')
    }
    $taskChild = [System.Diagnostics.Process]::Start($taskProcess)
    $taskChild.WaitForExit()
    if ($taskChild.ExitCode -ne 0) { throw 'Administrator provisioning failed.' }
    if ($DockerContainer) {
        & $taskProcess.FileName cp "${DockerContainer}:/tmp/rednote-admin-enrollment.json" $taskOutput
        if ($LASTEXITCODE -ne 0) { throw 'Unable to retrieve authenticator enrollment.' }
        & $taskProcess.FileName exec $DockerContainer rm /tmp/rednote-admin-enrollment.json
    }
    if ($IsWindows) {
        & icacls $taskOutput /inheritance:r /grant:r "$([System.Security.Principal.WindowsIdentity]::GetCurrent().Name):(F)" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Unable to protect the authenticator enrollment file.' }
    }
    Write-Host "Import AuthenticatorUri from $taskOutput into your authenticator and delete the file after enrollment."
}
finally {
    $taskBootstrap.Clear(); $taskProcess.Environment.Remove('AdminBootstrap__Password') | Out-Null
    $taskCredentials.Password = ''
}


# ./scripts/provision-admin.ps1 `
#   -Email 'test999@test999.com' `
#   -Roles ContentModerator,UserAdministrator,AuditReader
