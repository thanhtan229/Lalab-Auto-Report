using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LalabAutoReport.Infrastructure.Services;
using Xunit;

namespace LalabAutoReport.Tests;

public class UpdateServiceTests
{
    private class TestableGitHubUpdateService : GitHubUpdateService
    {
        private readonly Version _currentVersion;

        public TestableGitHubUpdateService(HttpClient client, Version currentVersion)
            : base(client, null, "thanhtan229", "Lalab-Auto-Report")
        {
            _currentVersion = currentVersion;
        }

        public override Version GetCurrentVersion() => _currentVersion;
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public MockHttpMessageHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_response);
        }
    }

    [Theory]
    [InlineData("v1.0", 1, 0)]
    [InlineData("v1.1", 1, 1)]
    [InlineData("v2.0.5", 2, 0)]
    [InlineData("1.0", 1, 0)]
    [InlineData("1.2.3.4", 1, 2)]
    public void ParseVersion_ValidTag_ReturnsExpectedVersion(string tag, int expectedMajor, int expectedMinor)
    {
        var v = GitHubUpdateService.ParseVersion(tag);
        v.Should().NotBeNull();
        v!.Major.Should().Be(expectedMajor);
        v.Minor.Should().Be(expectedMinor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("alpha-beta")]
    public void ParseVersion_InvalidTag_ReturnsNull(string? tag)
    {
        var v = GitHubUpdateService.ParseVersion(tag);
        v.Should().BeNull();
    }

    [Fact]
    public async Task CheckForUpdateAsync_NewVersionAvailable_ReturnsTrueAndAssetUrl()
    {
        var jsonResponse = @"{
            ""tag_name"": ""v1.1"",
            ""name"": ""Lalab Auto Report v1.1 - Cải tiến tốc độ"",
            ""body"": ""- Sửa lỗi A\n- Thêm tính năng B"",
            ""published_at"": ""2026-10-01T10:00:00Z"",
            ""assets"": [
                {
                    ""name"": ""LalabAutoReport.exe"",
                    ""size"": 176160768,
                    ""browser_download_url"": ""https://github.com/thanhtan229/Lalab-Auto-Report/releases/download/v1.1/LalabAutoReport.exe""
                }
            ]
        }";

        var responseMsg = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse)
        };

        var handler = new MockHttpMessageHandler(responseMsg);
        var client = new HttpClient(handler);
        var service = new TestableGitHubUpdateService(client, new Version(1, 0, 0, 0));

        var updateInfo = await service.CheckForUpdateAsync();

        updateInfo.IsUpdateAvailable.Should().BeTrue();
        updateInfo.LatestVersion.Should().Be("v1.1");
        updateInfo.ReleaseName.Should().Be("Lalab Auto Report v1.1 - Cải tiến tốc độ");
        updateInfo.ReleaseNotes.Should().Contain("Sửa lỗi A");
        updateInfo.DownloadUrl.Should().Be("https://github.com/thanhtan229/Lalab-Auto-Report/releases/download/v1.1/LalabAutoReport.exe");
        updateInfo.FileSizeBytes.Should().Be(176160768);
    }

    [Fact]
    public async Task CheckForUpdateAsync_SameVersion_ReturnsFalse()
    {
        var jsonResponse = @"{
            ""tag_name"": ""v1.0"",
            ""name"": ""Lalab Auto Report v1.0"",
            ""body"": ""Initial release"",
            ""assets"": []
        }";

        var responseMsg = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse)
        };

        var handler = new MockHttpMessageHandler(responseMsg);
        var client = new HttpClient(handler);
        var service = new TestableGitHubUpdateService(client, new Version(1, 0, 0, 0));

        var updateInfo = await service.CheckForUpdateAsync();

        updateInfo.IsUpdateAvailable.Should().BeFalse();
        updateInfo.LatestVersion.Should().Be("v1.0");
    }

    [Fact]
    public async Task CheckForUpdateAsync_NetworkError_HandlesGracefully()
    {
        var responseMsg = new HttpResponseMessage(HttpStatusCode.NotFound);
        var handler = new MockHttpMessageHandler(responseMsg);
        var client = new HttpClient(handler);
        var service = new TestableGitHubUpdateService(client, new Version(1, 0, 0, 0));

        var updateInfo = await service.CheckForUpdateAsync();

        updateInfo.IsUpdateAvailable.Should().BeFalse();
    }
}
