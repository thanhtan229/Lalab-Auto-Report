using System.Text;
using FluentAssertions;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Services;
using Xunit;

namespace LalabAutoReport.Tests;

public class MobileAuthServiceTests
{
    [Fact]
    public void GenerateAndValidateToken_AdminRole_ShouldSucceed()
    {
        var authService = new MobileAuthService();

        string token = authService.GenerateToken(MobileUserRole.Admin);

        token.Should().NotBeNullOrWhiteSpace();
        bool isValid = authService.TryValidateToken(token, out var role);

        isValid.Should().BeTrue();
        role.Should().Be(MobileUserRole.Admin);
    }

    [Fact]
    public void GenerateAndValidateToken_StaffRole_ShouldSucceed()
    {
        var authService = new MobileAuthService();

        string token = authService.GenerateToken(MobileUserRole.Staff);

        token.Should().NotBeNullOrWhiteSpace();
        bool isValid = authService.TryValidateToken(token, out var role);

        isValid.Should().BeTrue();
        role.Should().Be(MobileUserRole.Staff);
    }

    [Fact]
    public void ValidateToken_TamperedSignature_ShouldFail()
    {
        var authService = new MobileAuthService();
        string token = authService.GenerateToken(MobileUserRole.Admin);

        string[] parts = token.Split('.');
        string tamperedToken = parts[0] + ".badSignature123";

        bool isValid = authService.TryValidateToken(tamperedToken, out _);
        isValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateToken_InvalidOrEmpty_ShouldFail()
    {
        var authService = new MobileAuthService();

        authService.TryValidateToken("", out _).Should().BeFalse();
        authService.TryValidateToken("   ", out _).Should().BeFalse();
        authService.TryValidateToken("not_a_valid_token", out _).Should().BeFalse();
        authService.TryValidateToken("part1.part2.part3", out _).Should().BeFalse();
    }

    [Fact]
    public void AuthenticateWithPin_ShouldMatchSettings()
    {
        var authService = new MobileAuthService();
        var settings = new AppSettings
        {
            AdminPin = "999888",
            StaffPin = "111222"
        };

        authService.AuthenticateWithPin("999888", settings).Should().Be(MobileUserRole.Admin);
        authService.AuthenticateWithPin("111222", settings).Should().Be(MobileUserRole.Staff);
        authService.AuthenticateWithPin("wrong_pin", settings).Should().BeNull();
        authService.AuthenticateWithPin("", settings).Should().BeNull();
        authService.AuthenticateWithPin(null!, settings).Should().BeNull();
    }

    [Fact]
    public void AuthenticateWithPin_WhenAdminPinEmpty_ShouldReturnNull()
    {
        var authService = new MobileAuthService();
        var defaultSettings = new AppSettings();

        // Default AppSettings must not contain source-known hardcoded PINs
        defaultSettings.AdminPin.Should().BeEmpty();
        defaultSettings.StaffPin.Should().BeEmpty();

        // Any PIN attempt must fail closed
        authService.AuthenticateWithPin("123456", defaultSettings).Should().BeNull();
        authService.AuthenticateWithPin("000000", defaultSettings).Should().BeNull();
        authService.AuthenticateWithPin("888888", defaultSettings).Should().BeNull();
    }

    [Fact]
    public void AuthenticateWithPin_WhenPinsAreIdentical_ShouldReturnNull()
    {
        var authService = new MobileAuthService();
        var ambiguousSettings = new AppSettings
        {
            AdminPin = "123456",
            StaffPin = "123456"
        };

        // Identical PINs must fail closed to prevent ambiguous role assignment
        authService.AuthenticateWithPin("123456", ambiguousSettings).Should().BeNull();
    }

    [Fact]
    public void PersistentSecret_ShouldSurviveReinstantiation_AndValidateAcrossInstances()
    {
        var settingsRepo = new InMemorySettingsRepo();

        // Instance 1: Should generate secret and save to settings
        var instance1 = new MobileAuthService(settingsRepo);
        string token1 = instance1.GenerateToken(MobileUserRole.Admin);

        // Instance 2 (simulating app restart / new DI creation): Should load the saved secret
        var instance2 = new MobileAuthService(settingsRepo);

        // Token from instance 1 must validate successfully in instance 2
        bool isValid = instance2.TryValidateToken(token1, out var role);
        isValid.Should().BeTrue();
        role.Should().Be(MobileUserRole.Admin);
    }

    private class InMemorySettingsRepo : LalabAutoReport.Core.Interfaces.ISettingsRepository
    {
        public AppSettings Settings { get; set; } = new();

        public System.Threading.Tasks.Task<AppSettings> GetSettingsAsync(System.Threading.CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(Settings);

        public System.Threading.Tasks.Task SaveSettingsAsync(AppSettings settings, System.Threading.CancellationToken cancellationToken = default)
        {
            Settings = settings;
            return System.Threading.Tasks.Task.CompletedTask;
        }
    }
}
