# Uses only the synthetic profile created for local production acceptance.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$exe = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts\windows\LalabAutoReport.UI.exe'))
$profile = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts\acceptance\profile'))
$originalId = [int](Get-Content -LiteralPath (Join-Path $profile 'owned-pid.txt'))
$owned = Get-CimInstance Win32_Process -Filter "ProcessId=$originalId"
if (!$owned -or $owned.ExecutablePath -ne $exe -or !$owned.CommandLine.Contains($profile)) { throw 'Start the owned published fixture first.' }
$session = Invoke-RestMethod 'http://127.0.0.1:18789/api/auth/login' -Method Post -ContentType 'application/json' -Body '{"pin":"246810"}'
if (!$session.token) { throw 'Synthetic Admin login failed.' }
$headers = @{ Authorization = "Bearer $($session.token)" }
$before = Invoke-RestMethod 'http://127.0.0.1:18789/api/bills/1001' -Headers $headers
if ($before.grandTotal -ne 75000) { throw 'Fixture bill contract failed.' }
$duplicate = Start-Process -FilePath $exe -ArgumentList @('--isolated-data-directory', ('"' + $profile + '"'), '--minimized') -WorkingDirectory $projectRoot -WindowStyle Hidden -PassThru
if (!$duplicate.WaitForExit(15000) -or $duplicate.ExitCode -ne 0) { throw 'Duplicate did not hand off and exit.' }
$listener = Get-NetTCPConnection -LocalPort 18789 -State Listen
if (!($listener.OwningProcess -contains $originalId)) { throw 'Duplicate replaced the original listener.' }
& (Join-Path $PSScriptRoot 'Smoke-Published.ps1') -StopOnly
& (Join-Path $PSScriptRoot 'Smoke-Published.ps1')
$after = Invoke-RestMethod 'http://127.0.0.1:18789/api/bills/1001' -Headers $headers
if ($after.grandTotal -ne $before.grandTotal -or $after.customerName -ne $before.customerName) { throw 'Restart changed the locked bill.' }
$evidence = @{ singleInstanceIpc = 'PASS'; persistentLanLogin = 'PASS'; lockedBillAfterRestart = 'PASS'; oldPid = $originalId; newPid = [int](Get-Content -LiteralPath (Join-Path $profile 'owned-pid.txt')); fixture = 'isolated synthetic profile'; recordedUtc = [DateTime]::UtcNow.ToString('o') }
$evidence | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $projectRoot 'docs\production-evidence\published-runtime-acceptance.json')
Write-Output 'Published EXE: single-instance/IPC, restart, persistent LAN token and locked bill PASS.'
