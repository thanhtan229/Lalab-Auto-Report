param([switch]$StopOnly)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$exe = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts\windows\LalabAutoReport.UI.exe'))
$profile = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts\acceptance\profile'))
$pidFile = Join-Path $profile 'owned-pid.txt'
if ($StopOnly) {
    if (Test-Path -LiteralPath $pidFile) {
        $recordedId = [int](Get-Content -LiteralPath $pidFile)
        $owned = Get-CimInstance Win32_Process -Filter "ProcessId=$recordedId"
        if ($owned -and $owned.ExecutablePath -eq $exe -and $owned.CommandLine.Contains($profile)) {
            Stop-Process -Id $recordedId -Force
            Wait-Process -Id $recordedId -Timeout 15 -ErrorAction SilentlyContinue
            # Windows may briefly retain the listener record after the process exits.
            for ($attempt = 0; $attempt -lt 20; $attempt++) {
                $remaining = @(Get-NetTCPConnection -LocalPort 18789 -State Listen -ErrorAction SilentlyContinue)
                if (!($remaining.OwningProcess -contains $recordedId)) { break }
                Start-Sleep -Milliseconds 250
            }
        } elseif ($owned) { throw 'Recorded PID is not the isolated published fixture.' }
    }
    exit 0
}
if (!(Test-Path -LiteralPath (Join-Path $profile 'lalab_autoreport.db'))) { throw 'Seed the isolated fixture DB first.' }
if (Get-NetTCPConnection -LocalPort 18789 -State Listen -ErrorAction SilentlyContinue) { throw 'Fixture port already in use; do not terminate its owner.' }
$started = Start-Process -FilePath $exe -ArgumentList @('--isolated-data-directory', ('"' + $profile + '"'), '--minimized') -WorkingDirectory $projectRoot -WindowStyle Hidden -PassThru
$started.Id | Set-Content -LiteralPath $pidFile
for ($attempt = 0; $attempt -lt 20; $attempt++) {
    Start-Sleep -Seconds 1
    if ($started.HasExited) { throw 'Published application exited unexpectedly.' }
    try { $health = Invoke-RestMethod 'http://127.0.0.1:18789/api/status' -TimeoutSec 2 } catch { continue }
    $listener = Get-NetTCPConnection -LocalPort 18789 -State Listen -ErrorAction Stop
    if ($health.appName -ne 'Lalab Auto Report' -or !($listener.OwningProcess -contains $started.Id)) { throw 'Fixture health ownership mismatch.' }
    Write-Output "Published fixture PID $($started.Id): health OK"
    exit 0
}
throw 'Published fixture health timed out.'
