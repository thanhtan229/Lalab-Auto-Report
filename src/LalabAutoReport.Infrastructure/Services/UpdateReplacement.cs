using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace LalabAutoReport.Infrastructure.Services;

/// <summary>Stages replacement next to the executable; failure preserves the original and download.</summary>
public static class UpdateReplacement
{
    public static async Task<string> PrepareAsync(string source, string destination, int ownerPid,
        string restartArguments, bool restart = true, CancellationToken cancellationToken = default)
    {
        source = Path.GetFullPath(source);
        destination = Path.GetFullPath(destination);
        if (source.Equals(destination, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Update source and destination must differ.");
        await using var input = File.OpenRead(source);
        if (input.Length < 64 || input.ReadByte() != 'M' || input.ReadByte() != 'Z')
            throw new InvalidDataException("Bản cập nhật không phải Windows executable hợp lệ.");
        input.Position = 0x3c;
        using (var reader = new BinaryReader(input, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            int header = reader.ReadInt32();
            if (header < 64 || header > input.Length - 4)
                throw new InvalidDataException("Bản cập nhật thiếu PE header.");
            input.Position = header;
            if (reader.ReadUInt32() != 0x00004550)
                throw new InvalidDataException("Bản cập nhật có PE header không hợp lệ.");
        }
        input.Position = 0;
        string hash = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken));
        string directory = Path.GetDirectoryName(source)!;
        string suffix = Guid.NewGuid().ToString("N");
        var plan = new
        {
            source, destination, ownerPid, restartArguments, restart, sha256 = hash,
            staged = destination + ".update-" + suffix,
            backup = destination + ".previous-" + suffix,
            failed = destination + ".failed-" + suffix,
            log = Path.Combine(directory, "apply-update.log")
        };
        await File.WriteAllTextAsync(Path.Combine(directory, "update-plan.json"), JsonSerializer.Serialize(plan), cancellationToken);
        string script = Path.Combine(directory, "apply-update.ps1");
        await File.WriteAllTextAsync(script, Script, cancellationToken);
        return script;
    }

    public static ProcessStartInfo CreateStartInfo(string script) => new()
    {
        FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
        Arguments = "-NoProfile -NonInteractive -File " + QuoteArgument(script),
        UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
    };

    public static string QuoteArgument(string value)
    {
        var result = new System.Text.StringBuilder("\"");
        int slashes = 0;
        foreach (char character in value)
        {
            if (character == '\\') { slashes++; continue; }
            result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            result.Append(character);
            slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    private const string Script = """
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
        """;
}
