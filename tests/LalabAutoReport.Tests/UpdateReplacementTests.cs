using System.Diagnostics;
using System.IO;
using FluentAssertions;
using LalabAutoReport.Infrastructure.Services;

namespace LalabAutoReport.Tests;

public sealed class UpdateReplacementTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "lalab-update-test-" + Guid.NewGuid().ToString("N"));
    private readonly string _source;
    private readonly string _destination;

    public UpdateReplacementTests()
    {
        Directory.CreateDirectory(_directory);
        _source = Path.Combine(_directory, "download.exe");
        _destination = Path.Combine(_directory, "installed.exe");
        File.Copy(typeof(UpdateReplacement).Assembly.Location, _source);
        File.WriteAllText(_destination, "original executable fixture");
    }

    private static async Task<int> RunAsync(string script)
    {
        using var process = Process.Start(UpdateReplacement.CreateStartInfo(script))!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await process.WaitForExitAsync(timeout.Token);
        return process.ExitCode;
    }

    [Fact]
    public async Task Replace_AtomicallyKeepsPreviousAndDownload()
    {
        string script = await UpdateReplacement.PrepareAsync(_source, _destination, 0, "", restart: false);
        (await RunAsync(script)).Should().Be(0, File.ReadAllText(Path.Combine(_directory, "apply-update.log")));
        File.ReadAllBytes(_destination).Should().Equal(File.ReadAllBytes(_source));
        File.ReadAllText(Directory.GetFiles(_directory, "*.previous-*").Single()).Should().Be("original executable fixture");
        File.Exists(_source).Should().BeTrue();
    }

    [Fact]
    public async Task LockedDestination_LeavesOriginalAndDownloadForRetry()
    {
        string script = await UpdateReplacement.PrepareAsync(_source, _destination, 0, "", restart: false);
        using (var locked = new FileStream(_destination, FileMode.Open, FileAccess.Read, FileShare.None))
            (await RunAsync(script)).Should().Be(1);
        File.ReadAllText(_destination).Should().Be("original executable fixture");
        File.Exists(_source).Should().BeTrue();
        File.ReadAllText(Path.Combine(_directory, "apply-update.log")).Should().Contain("FAILED");
    }

    [Fact]
    public async Task ChangedDownload_IsRefusedBeforeReplacement()
    {
        string script = await UpdateReplacement.PrepareAsync(_source, _destination, 0, "", restart: false);
        File.AppendAllText(_source, "changed after download");
        (await RunAsync(script)).Should().Be(1);
        File.ReadAllText(_destination).Should().Be("original executable fixture");
    }

    [Fact]
    public async Task UnrelatedOwnerPid_IsNeverWaitedOnOrTerminated()
    {
        string script = await UpdateReplacement.PrepareAsync(_source, _destination, Environment.ProcessId, "", restart: false);
        (await RunAsync(script)).Should().Be(1);
        File.ReadAllText(_destination).Should().Be("original executable fixture");
        File.ReadAllText(Path.Combine(_directory, "apply-update.log")).Should().Contain("another executable");
    }

    [Fact]
    public async Task InvalidDownload_IsRefusedBeforeUpdaterIsCreated()
    {
        File.WriteAllText(_source, "truncated HTTP error body");
        await Assert.ThrowsAsync<InvalidDataException>(() => UpdateReplacement.PrepareAsync(_source, _destination, 0, ""));
        File.ReadAllText(_destination).Should().Be("original executable fixture");
        File.Exists(Path.Combine(_directory, "apply-update.ps1")).Should().BeFalse();
    }

    [Fact]
    public async Task FailedRestart_RestoresOriginalAndKeepsFailedCandidate()
    {
        string script = await UpdateReplacement.PrepareAsync(_source, _destination, 0, "", restart: true);
        // Fixture is a library, so Windows cannot launch it as an application.
        (await RunAsync(script)).Should().Be(1);
        File.ReadAllText(_destination).Should().Be("original executable fixture");
        Directory.GetFiles(_directory, "*.failed-*").Should().ContainSingle(File.ReadAllText(Path.Combine(_directory, "apply-update.log")));
        File.Exists(_source).Should().BeTrue();
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
