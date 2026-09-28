@echo off
setlocal
title Lalab Auto Report - Run Tests

echo ===================================================
echo   Chay toan bo test suite Lalab Auto Report
echo ===================================================
dotnet test --nologo
if errorlevel 1 (
    echo [FAIL] Co test case bi loi.
    exit /b 1
) else (
    echo [SUCCESS] Tat ca 52 test cases deu vuot qua.
    exit /b 0
)
