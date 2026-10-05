using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Infrastructure.Services;
using Xunit;

namespace LalabAutoReport.Tests;

public sealed class LiveAutoUpdateAcceptanceTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _installedAppDir;
    private readonly string _stagedProfileDir;
    private readonly string _installedExe;
    private readonly HttpClient _httpClient;

    public LiveAutoUpdateAcceptanceTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "lalab-live-update-" + Guid.NewGuid().ToString("N"));
        _installedAppDir = Path.Combine(_testRoot, "app");
        _stagedProfileDir = Path.Combine(_testRoot, "profile");
        Directory.CreateDirectory(_installedAppDir);
        Directory.CreateDirectory(_stagedProfileDir);

        _installedExe = Path.Combine(_installedAppDir, "LalabAutoReport.exe");
        File.WriteAllText(_installedExe, "v1.0 original executable fixture");

        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "LalabAutoReport-Updater-LiveTest");
    }

    private class StagedUpdateService : GitHubUpdateService
    {
        private readonly Version _mockCurrentVersion;

        public StagedUpdateService(HttpClient client, Version mockCurrentVersion)
            : base(client, null, "thanhtan229", "Lalab-Auto-Report")
        {
            _mockCurrentVersion = mockCurrentVersion;
        }

        public override Version GetCurrentVersion() => _mockCurrentVersion;
    }

    [Fact]
    public async Task LiveGitHub_WhenCurrentIsV1_DetectsV2ReleaseAndAssets()
    {
        var service = new StagedUpdateService(_httpClient, new Version(1, 0, 0, 0));
        var updateInfo = await service.CheckForUpdateAsync();

        updateInfo.IsUpdateAvailable.Should().BeTrue("v2.0 should be detected when current version is 1.0.0.0");
        updateInfo.LatestVersion.Should().Be("v2.0");
        updateInfo.ReleaseName.Should().Contain("v2.0");
        updateInfo.DownloadUrl.Should().NotBeNullOrWhiteSpace();
        updateInfo.DownloadUrl.Should().EndWith(".exe");
        updateInfo.FileSizeBytes.Should().Be(203004332);
    }

    [Fact]
    public async Task LiveGitHub_WhenCurrentIsV2_ReportsNoUpdate()
    {
        var service = new StagedUpdateService(_httpClient, new Version(2, 0, 0, 0));
        var updateInfo = await service.CheckForUpdateAsync();

        updateInfo.IsUpdateAvailable.Should().BeFalse("App already on v2.0 should not prompt for update");
        updateInfo.LatestVersion.Should().Be("v2.0");
    }

    [Fact]
    public async Task LiveGitHub_DownloadAndVerifyChecksum_MatchesPublishedRelease()
    {
        var service = new StagedUpdateService(_httpClient, new Version(1, 0, 0, 0));
        var updateInfo = await service.CheckForUpdateAsync();
        updateInfo.IsUpdateAvailable.Should().BeTrue();

        string tempDownload = Path.Combine(_testRoot, "downloaded.exe");
        using (var response = await _httpClient.GetAsync(updateInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            await using var content = await response.Content.ReadAsStreamAsync();
            await using var file = File.Create(tempDownload);
            await content.CopyToAsync(file);
        }

        new FileInfo(tempDownload).Length.Should().Be(updateInfo.FileSizeBytes);

        await using var stream = File.OpenRead(tempDownload);
        string hash = Convert.ToHexString(await SHA256.HashDataAsync(stream));
        hash.Should().Be("66F7D9D9F4EA2F2AA627624D15D9914566C302064CED76363438B6E533F95D4F");

        // Validate PE headers via UpdateReplacement.PrepareAsync
        string script = await UpdateReplacement.PrepareAsync(tempDownload, _installedExe, 0, "", restart: false);
        File.Exists(script).Should().BeTrue();

        // Execute updater script
        var startInfo = UpdateReplacement.CreateStartInfo(script);
        using var process = Process.Start(startInfo)!;
        await process.WaitForExitAsync();
        process.ExitCode.Should().Be(0);

        // Verify target replaced
        string installedHash = Convert.ToHexString(await SHA256.HashDataAsync(File.OpenRead(_installedExe)));
        installedHash.Should().Be("66F7D9D9F4EA2F2AA627624D15D9914566C302064CED76363438B6E533F95D4F");

        // Verify backup
        string backupFile = Directory.GetFiles(_installedAppDir, "*.previous-*").Should().ContainSingle().Subject;
        File.ReadAllText(backupFile).Should().Be("v1.0 original executable fixture");
    }

    [Fact]
    public async Task FailureRecovery_TamperedDownload_IsRefusedAndOriginalPreserved()
    {
        string tamperedSource = Path.Combine(_testRoot, "tampered.exe");
        // Create valid PE from test assembly
        File.Copy(typeof(UpdateReplacement).Assembly.Location, tamperedSource);

        string script = await UpdateReplacement.PrepareAsync(tamperedSource, _installedExe, 0, "", restart: false);

        // Tamper source after plan was generated
        await File.AppendAllTextAsync(tamperedSource, "tampered-content");

        var startInfo = UpdateReplacement.CreateStartInfo(script);
        using var process = Process.Start(startInfo)!;
        await process.WaitForExitAsync();
        process.ExitCode.Should().Be(1);

        // Original executable must remain unchanged
        File.ReadAllText(_installedExe).Should().Be("v1.0 original executable fixture");
    }

    [Fact]
    public async Task FailureRecovery_LockedDestination_IsSafelyPreservedForRetry()
    {
        string validSource = Path.Combine(_testRoot, "valid.exe");
        File.Copy(typeof(UpdateReplacement).Assembly.Location, validSource);

        string script = await UpdateReplacement.PrepareAsync(validSource, _installedExe, 0, "", restart: false);

        // Lock destination
        using (var locked = new FileStream(_installedExe, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var startInfo = UpdateReplacement.CreateStartInfo(script);
            using var process = Process.Start(startInfo)!;
            await process.WaitForExitAsync();
            process.ExitCode.Should().Be(1);
        }

        File.ReadAllText(_installedExe).Should().Be("v1.0 original executable fixture");
        File.Exists(validSource).Should().BeTrue();
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        try { Directory.Delete(_testRoot, recursive: true); } catch { }
    }
}
