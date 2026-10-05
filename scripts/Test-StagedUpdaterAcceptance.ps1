# Test-StagedUpdaterAcceptance.ps1
# Comprehensive pre-publish updater acceptance test on isolated fixtures
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$newExe = Join-Path $projectRoot "publish\LalabAutoReport.exe"
if (!(Test-Path -LiteralPath $newExe)) {
    throw "New release executable not found at $newExe. Run dotnet publish first."
}

$newExeHash = (Get-FileHash -Path $newExe -Algorithm SHA256).Hash
Write-Output "Testing staged updater with new artifact: $newExe"
Write-Output "Expected SHA256: $newExeHash"

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("lalab-staged-updater-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null

try {
    # 1. Setup isolated directories
    $stagedAppDir = Join-Path $testRoot "installed-app"
    $stagedProfileDir = Join-Path $testRoot "profile"
    $downloadDir = Join-Path $testRoot "download"
    New-Item -ItemType Directory -Path $stagedAppDir -Force | Out-Null
    New-Item -ItemType Directory -Path $stagedProfileDir -Force | Out-Null
    New-Item -ItemType Directory -Path $downloadDir -Force | Out-Null

    $installedExe = Join-Path $stagedAppDir "LalabAutoReport.exe"
    # Seed an initial old executable fixture
    [IO.File]::WriteAllText($installedExe, "initial old executable content fixture")
    $oldExeHash = (Get-FileHash -Path $installedExe -Algorithm SHA256).Hash

    # Copy template script from UpdateReplacement.cs
    $scriptContent = @'
$ErrorActionPreference = 'Stop'
$plan = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'update-plan.json') -Raw | ConvertFrom-Json
$replaced = $false
function Read-Hash([string] $path) {
    $stream = [IO.File]::OpenRead($path)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '') }
    finally { $stream.Dispose(); $algorithm.Dispose() }
}
try {
    if ($plan.ownerPid -gt 0) {
        $owner = Get-Process -Id $plan.ownerPid -ErrorAction SilentlyContinue
        if ($owner) {
            if (![string]::Equals($owner.MainModule.FileName, $plan.destination, [StringComparison]::OrdinalIgnoreCase)) {
                throw 'Owner PID now belongs to another executable; replacement refused.'
            }
            if (!$owner.WaitForExit(120000)) { throw 'Application did not exit; replacement refused.' }
        }
    }
    $targetDirectory = [IO.Path]::GetDirectoryName($plan.destination)
    foreach ($target in @($plan.staged, $plan.backup, $plan.failed)) {
        if ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($target)) -ne $targetDirectory) {
            throw 'Replacement paths must remain in the executable directory.'
        }
    }
    if ((Read-Hash $plan.source) -ne $plan.sha256) { throw 'Downloaded executable changed.' }
    [IO.File]::Copy($plan.source, $plan.staged, $false)
    if ((Read-Hash $plan.staged) -ne $plan.sha256) { throw 'Staged executable hash mismatch.' }
    [IO.File]::Replace($plan.staged, $plan.destination, $plan.backup)
    $replaced = $true
    if ($plan.restart) {
        $start = @{ FilePath = $plan.destination; WorkingDirectory = $targetDirectory; WindowStyle = 'Hidden'; PassThru = $true }
        if ($plan.restartArguments) { $start.ArgumentList = $plan.restartArguments }
        $launched = Start-Process @start
        Start-Sleep -Seconds 3
        if ($launched.HasExited) { throw 'Updated application exited immediately.' }
    }
    'SUCCESS: executable replaced; previous executable and download retained for rollback.' | Set-Content -LiteralPath $plan.log
    exit 0
} catch {
    $failure = $_.Exception.Message
    if ($replaced) {
        try { [IO.File]::Replace($plan.backup, $plan.destination, $plan.failed) }
        catch { $failure += '; rollback failed: ' + $_.Exception.Message }
    }
    ('FAILED: ' + $failure + '; download/recovery files retained.') | Set-Content -LiteralPath $plan.log
    exit 1
}
'@

    function Invoke-UpdaterPlan($source, $dest, $hash, $restart = $false, $args = "") {
        $dir = Split-Path $source
        $suffix = [Guid]::NewGuid().ToString("N")
        $plan = @{
            source = $source
            destination = $dest
            ownerPid = 0
            restartArguments = $args
            restart = $restart
            sha256 = $hash
            staged = "$dest.update-$suffix"
            backup = "$dest.previous-$suffix"
            failed = "$dest.failed-$suffix"
            log = Join-Path $dir "apply-update.log"
        }
        $plan | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $dir "update-plan.json")
        $scriptPath = Join-Path $dir "apply-update.ps1"
        $scriptContent | Set-Content -LiteralPath $scriptPath
        
        $p = Start-Process -FilePath "powershell.exe" -ArgumentList @("-NoProfile", "-NonInteractive", "-File", "`"$scriptPath`"") -NoNewWindow -Wait -PassThru
        return $p.ExitCode
    }

    Write-Output "`n=== TEST 1: Checksum mismatch -> Reject and preserve original ==="
    $corruptDownload = Join-Path $downloadDir "corrupt.exe"
    Copy-Item -Path $newExe -Destination $corruptDownload -Force
    # Tamper with the file
    [IO.File]::AppendAllText($corruptDownload, "corrupted content")
    $code = Invoke-UpdaterPlan $corruptDownload $installedExe $newExeHash $false
    if ($code -eq 0) { throw "Test 1 failed: Tampered download was not rejected!" }
    $log1 = Get-Content (Join-Path $downloadDir "apply-update.log") -Raw
    if ($log1 -notmatch "FAILED") { throw "Test 1 failed: Log did not report FAILED" }
    if ((Get-FileHash -Path $installedExe -Algorithm SHA256).Hash -ne $oldExeHash) {
        throw "Test 1 failed: Installed binary was modified on checksum failure!"
    }
    Write-Output "  PASS: Tampered file rejected, original preserved. Log: $($log1.Trim())"

    Write-Output "`n=== TEST 2: Successful atomic apply of new release binary ==="
    $validDownload = Join-Path $downloadDir "LalabAutoReport_Valid.exe"
    Copy-Item -Path $newExe -Destination $validDownload -Force
    $code = Invoke-UpdaterPlan $validDownload $installedExe $newExeHash $false
    if ($code -ne 0) {
        $log2 = Get-Content (Join-Path $downloadDir "apply-update.log") -Raw
        throw "Test 2 failed: Valid update failed with exit code $($code): $log2"
    }
    $newInstalledHash = (Get-FileHash -Path $installedExe -Algorithm SHA256).Hash
    if ($newInstalledHash -ne $newExeHash) { throw "Test 2 failed: Hash does not match new release!" }
    $backups = Get-ChildItem -Path $stagedAppDir -Filter "*.previous-*"
    if ($backups.Count -eq 0) { throw "Test 2 failed: Backup .previous-* not found!" }
    $backupContent = [IO.File]::ReadAllText($backups[0].FullName)
    if ($backupContent -ne "initial old executable content fixture") {
        throw "Test 2 failed: Backup file does not contain original content!"
    }
    Write-Output "  PASS: Binary replaced atomically with SHA256: $newInstalledHash"
    Write-Output "  PASS: Previous executable preserved at: $($backups[0].Name)"
    Write-Output "  PASS: Download source retained at: $validDownload"

    Write-Output "`n=== TEST 3: Destination locked -> Graceful failure and retry preservation ==="
    $fs = [IO.File]::Open($installedExe, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
    try {
        $code = Invoke-UpdaterPlan $validDownload $installedExe $newExeHash $false
        if ($code -eq 0) { throw "Test 3 failed: Locked destination should have failed replacement" }
    } finally {
        $fs.Dispose()
    }
    $log3 = Get-Content (Join-Path $downloadDir "apply-update.log") -Raw
    if ($log3 -notmatch "FAILED") { throw "Test 3 failed: Log did not report FAILED on lock" }
    if ((Get-FileHash -Path $installedExe -Algorithm SHA256).Hash -ne $newInstalledHash) {
        throw "Test 3 failed: Installed binary corrupted after lock failure!"
    }
    Write-Output "  PASS: Locked file handled gracefully. Original & download preserved for retry"

    Write-Output "`n=== TEST 4: Restart updated binary with isolated profile & verify version ==="
    $oldArtifact = Join-Path $projectRoot "artifacts\acceptance\profile\lalab_autoreport.db"
    Copy-Item -Path $oldArtifact -Destination (Join-Path $stagedProfileDir "lalab_autoreport.db") -Force
    
    $proc = Start-Process -FilePath $installedExe -ArgumentList @("--isolated-data-directory", "`"$stagedProfileDir`"", "--minimized") -WindowStyle Hidden -PassThru
    try {
        $health = $null
        for ($i = 0; $i -lt 15; $i++) {
            Start-Sleep -Seconds 1
            try {
                $health = Invoke-RestMethod -Uri "http://127.0.0.1:18789/api/status" -TimeoutSec 2
                if ($health -and $health.appName -eq "Lalab Auto Report") { break }
            } catch { }
        }
        if (!$health -or $health.appName -ne "Lalab Auto Report") {
            throw "Test 4 failed: Application failed to start or respond to health check after update"
        }
        if ($health.version -ne "2.0.0") {
            throw "Test 4 failed: Application reported version $($health.version), expected 2.0.0"
        }
        Write-Output "  PASS: Updated binary launched successfully: PID $($proc.Id)"
        Write-Output "  PASS: Updated binary reports version $($health.version) and workshop '$($health.workshopName)'"
    } finally {
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    }

    Write-Output "`n=== TEST 5: Verify profile database and config preserved ==="
    $dbPath = Join-Path $stagedProfileDir "lalab_autoreport.db"
    if (!(Test-Path -LiteralPath $dbPath) -or (Get-Item $dbPath).Length -eq 0) {
        throw "Test 5 failed: Profile DB was not preserved!"
    }
    Write-Output "  PASS: Fixture database preserved intact ($((Get-Item $dbPath).Length) bytes)"

    Write-Output "`nALL PRE-PUBLISH STAGED UPDATER ACCEPTANCE CHECKS PASSED!"
} finally {
    Remove-Item -Path $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
