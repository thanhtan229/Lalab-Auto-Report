@echo off
setlocal
title Lalab Auto Report - Restart Dev

echo ===================================================
echo   Lalab Auto Report - Fast Development Restart
echo ===================================================

:: 1. Dung cac tien trinh ung dung cu
echo [1/4] Dang dung cac tien trinh ung dung cu...
powershell -NoProfile -Command "Get-Process -Name 'LalabAutoReport.UI' -ErrorAction SilentlyContinue | Stop-Process -Force; Get-CimInstance Win32_Process -Filter \"CommandLine LIKE '%%LalabAutoReport.UI%%' AND Name LIKE 'dotnet%%'\" -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }"
ping -n 2 127.0.0.1 >nul

:: 2. Build Debug (khong package exe)
echo [2/4] Dang build ma nguon che do Debug...
dotnet build src\LalabAutoReport.UI\LalabAutoReport.UI.csproj -c Debug --nologo -v q
if errorlevel 1 (
    echo [ERROR] Build that bai! Vui long kiem tra loi bien dich phia tren.
    exit /b 1
)

:: 3. Khoi dong ung dung tu development build
echo [3/4] Dang khoi dong Lalab Auto Report UI...
start "" dotnet run --project src\LalabAutoReport.UI -c Debug --no-build

:: 4. Health Check xac nhan ung dung dang hoat dong
echo [4/4] Dang kiem tra trang thai ung dung (Health Check)...
ping -n 4 127.0.0.1 >nul

powershell -NoProfile -Command ^
    "$proc = Get-Process -Name 'LalabAutoReport.UI' -ErrorAction SilentlyContinue; " ^
    "if (-not $proc) { " ^
    "    $dotnetProc = Get-CimInstance Win32_Process -Filter \"CommandLine LIKE '%%LalabAutoReport.UI%%' AND Name LIKE 'dotnet%%'\" -ErrorAction SilentlyContinue; " ^
    "    if ($dotnetProc) { $procId = $dotnetProc.ProcessId } " ^
    "} else { $procId = $proc.Id }; " ^
    "if ($procId) { " ^
    "    Write-Host '[SUCCESS] Lalab Auto Report dang chay thanh cong (PID: ' $procId ')!' -ForegroundColor Green; " ^
    "    Write-Host '          Ung dung san sang de kiem thu.' -ForegroundColor Green; " ^
    "    exit 0; " ^
    "} else { " ^
    "    Write-Host '[FAIL] Ung dung khong ton tai hoac crash sau khi mo.' -ForegroundColor Red; " ^
    "    Write-Host '       Kiem tra log tai: %LocalAppData%\LalabAutoReport\logs' -ForegroundColor Red; " ^
    "    exit 1; " ^
    "}"

if errorlevel 1 (
    echo [WARNING] Health check that bai!
    exit /b 1
)

echo ===================================================
echo   San sang de test!
echo ===================================================
