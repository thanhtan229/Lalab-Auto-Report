@echo off
setlocal
title Lalab Auto Report - Stop Dev

echo Dang dung cac tien trinh Lalab Auto Report...
powershell -NoProfile -Command "Get-Process -Name 'LalabAutoReport.UI' -ErrorAction SilentlyContinue | Stop-Process -Force; Get-CimInstance Win32_Process -Filter \"CommandLine LIKE '%%LalabAutoReport.UI%%' AND Name LIKE 'dotnet%%'\" -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }"
echo [DONE] Toan bo tien trinh Lalab Auto Report da duoc giai phong sach se.
