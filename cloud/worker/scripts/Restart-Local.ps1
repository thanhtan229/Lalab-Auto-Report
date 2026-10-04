param([switch]$StopOnly)
$ErrorActionPreference = 'Stop'
$workerRoot = Split-Path $PSScriptRoot -Parent
$entry = [IO.Path]::GetFullPath((Join-Path $workerRoot 'node_modules\wrangler\bin\wrangler.js'))
$owned = @(Get-CimInstance Win32_Process -Filter "Name='node.exe'" | Where-Object {
    $_.CommandLine -and $_.CommandLine.Contains($entry) -and $_.CommandLine -match '\sdev(\s|$)'
})
foreach ($process in $owned) {
    Write-Output "Stopping owned local Wrangler tree PID $($process.ProcessId)"
    & taskkill.exe /PID $process.ProcessId /T /F | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Owned Wrangler stop failed.' }
}
if ($StopOnly) { exit 0 }
$node = (Get-Command node.exe).Source
$out = Join-Path $env:TEMP 'lalab-worker-local.out.log'
$err = Join-Path $env:TEMP 'lalab-worker-local.err.log'
$started = Start-Process -FilePath $node -ArgumentList @('"' + $entry + '"', 'dev', '--local', '--ip', '127.0.0.1', '--port', '18787', '--inspector-port', '18788') -WorkingDirectory $workerRoot -WindowStyle Hidden -RedirectStandardOutput $out -RedirectStandardError $err -PassThru
for ($attempt = 0; $attempt -lt 20; $attempt++) {
    Start-Sleep -Seconds 1
    if ($started.HasExited) { throw "Wrangler exited; inspect $err" }
    try {
        $health = Invoke-RestMethod 'http://127.0.0.1:18787/api/health' -TimeoutSec 2
        if ($health.appName -eq 'Lalab Cloud Worker 24/7') {
            $listenerPids = @(Get-NetTCPConnection -LocalPort 18787 -State Listen -ErrorAction Stop | Select-Object -ExpandProperty OwningProcess -Unique)
            $ownsListener = $false
            foreach ($listenerPid in $listenerPids) {
                $ancestorId = $listenerPid
                for ($depth = 0; $depth -lt 12 -and $ancestorId -gt 0; $depth++) {
                    if ($ancestorId -eq $started.Id) { $ownsListener = $true; break }
                    $ancestor = Get-CimInstance Win32_Process -Filter "ProcessId=$ancestorId"
                    if (!$ancestor) { break }
                    $ancestorId = $ancestor.ParentProcessId
                }
            }
            if (!$ownsListener) { throw 'Local Worker listener does not belong to the started Wrangler tree.' }
            Write-Output "Local Worker PID $($started.Id), status $($health.status), configurationReady=$($health.configurationReady)"
            exit 0
        }
    } catch { }
}
throw "Local Worker health timed out; inspect $out and $err"
