@echo off
setlocal
title Lalab Auto Report - Development Status

echo ===================================================
echo   Lalab Auto Report - Runtime Status ^& Health Check
echo ===================================================

powershell -NoProfile -Command ^
    "$proc = Get-Process -Name 'LalabAutoReport.UI' -ErrorAction SilentlyContinue; " ^
    "if (-not $proc) { " ^
    "    $dotnetProc = Get-CimInstance Win32_Process -Filter \"CommandLine LIKE '%%LalabAutoReport.UI%%' AND Name LIKE 'dotnet%%'\" -ErrorAction SilentlyContinue; " ^
    "    if ($dotnetProc) { $procId = $dotnetProc.ProcessId; $procName = $dotnetProc.Name } " ^
    "} else { $procId = $proc.Id; $procName = $proc.ProcessName }; " ^
    "if ($procId) { " ^
    "    Write-Host '[STATUS] Application is RUNNING' -ForegroundColor Green; " ^
    "    Write-Host ('  Process ID    : ' + $procId); " ^
    "    Write-Host ('  Process Name  : ' + $procName); " ^
    "    $liveProc = Get-Process -Id $procId -ErrorAction SilentlyContinue; " ^
    "    if ($liveProc) { " ^
    "        Write-Host ('  Memory (WS)   : ' + [math]::Round($liveProc.WorkingSet64 / 1MB, 2) + ' MB'); " ^
    "        Write-Host ('  Start Time    : ' + $liveProc.StartTime.ToString('yyyy-MM-dd HH:mm:ss')); " ^
    "    } " ^
    "    $dbPath = Join-Path $env:LOCALAPPDATA 'LalabAutoReport\lalab_autoreport.db'; " ^
    "    if (Test-Path $dbPath) { " ^
    "        $dbSize = (Get-Item $dbPath).Length; " ^
    "        Write-Host ('  Database      : ' + $dbPath + ' (' + [math]::Round($dbSize / 1KB, 1) + ' KB)'); " ^
    "    } else { " ^
    "        Write-Host ('  Database      : Not yet initialized') -ForegroundColor Yellow; " ^
    "    } " ^
    "    $logDir = Join-Path $env:LOCALAPPDATA 'LalabAutoReport\logs'; " ^
    "    if (Test-Path $logDir) { " ^
    "        $latestLog = Get-ChildItem -Path $logDir -Filter '*.log' | Sort-Object LastWriteTime -Descending | Select-Object -First 1; " ^
    "        if ($latestLog) { " ^
    "            Write-Host ('  Latest Log    : ' + $latestLog.FullName + ' (' + [math]::Round($latestLog.Length / 1KB, 1) + ' KB)'); " ^
    "        } " ^
    "    } " ^
    "    exit 0; " ^
    "} else { " ^
    "    Write-Host '[STATUS] Application is STOPPED (No running instances)' -ForegroundColor Yellow; " ^
    "    $dbPath = Join-Path $env:LOCALAPPDATA 'LalabAutoReport\lalab_autoreport.db'; " ^
    "    if (Test-Path $dbPath) { " ^
    "        $dbSize = (Get-Item $dbPath).Length; " ^
    "        Write-Host ('  Database      : ' + $dbPath + ' (' + [math]::Round($dbSize / 1KB, 1) + ' KB)'); " ^
    "    } else { " ^
    "        Write-Host ('  Database      : Not yet initialized') -ForegroundColor DarkGray; " ^
    "    } " ^
    "    $logDir = Join-Path $env:LOCALAPPDATA 'LalabAutoReport\logs'; " ^
    "    if (Test-Path $logDir) { " ^
    "        $latestLog = Get-ChildItem -Path $logDir -Filter '*.log' | Sort-Object LastWriteTime -Descending | Select-Object -First 1; " ^
    "        if ($latestLog) { " ^
    "            Write-Host ('  Latest Log    : ' + $latestLog.FullName + ' (' + [math]::Round($latestLog.Length / 1KB, 1) + ' KB)'); " ^
    "        } " ^
    "    } " ^
    "    exit 0; " ^
    "}"
