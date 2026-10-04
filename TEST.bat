@echo off
setlocal
title Lalab Auto Report - Run Tests

echo ===================================================
echo   Chay toan bo test suite Lalab Auto Report
echo ===================================================

:: Dung bat ky tien trinh testhost cu dang giu file dll
taskkill /F /IM testhost.exe >nul 2>&1

dotnet test --nologo
if errorlevel 1 (
    echo [FAIL] Co test case bi loi.
    exit /b 1
) else (
    echo [SUCCESS] Tat ca cac test case deu vuot qua thanh cong!
    exit /b 0
)
