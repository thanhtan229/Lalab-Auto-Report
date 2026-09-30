# Script go bo menu chuot phai QUICK BILL & DA IN khoi Windows Explorer
$dirKey = "HKCU:\Software\Classes\Directory\shell\LalabQuickBill"
$bgKey = "HKCU:\Software\Classes\Directory\Background\shell\LalabQuickBill"
$printDirKey = "HKCU:\Software\Classes\Directory\shell\LalabTogglePrinted"
$printBgKey = "HKCU:\Software\Classes\Directory\Background\shell\LalabTogglePrinted"

if (Test-Path $dirKey) {
    Remove-Item -Path $dirKey -Recurse -Force
}

if (Test-Path $bgKey) {
    Remove-Item -Path $bgKey -Recurse -Force
}

if (Test-Path $printDirKey) {
    Remove-Item -Path $printDirKey -Recurse -Force
}

if (Test-Path $printBgKey) {
    Remove-Item -Path $printBgKey -Recurse -Force
}

Write-Host "[SUCCESS] Da go bo menu chuot phai QUICK BILL va ĐÃ IN thanh cong!" -ForegroundColor Green
