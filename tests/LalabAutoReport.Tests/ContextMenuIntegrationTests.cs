using System;
using System.IO;
using FluentAssertions;
using LalabAutoReport.Infrastructure.Windows;
using Microsoft.Win32;
using Xunit;

namespace LalabAutoReport.Tests;

public class ContextMenuIntegrationTests : IDisposable
{
    private readonly WindowsContextMenuIntegrationService _service;

    public ContextMenuIntegrationTests()
    {
        _service = new WindowsContextMenuIntegrationService();
    }

    [Fact]
    public void GenerateRegFileContent_ShouldContainExpectedKeysAndEscapedCommands()
    {
        // Act
        string regContent = _service.GenerateRegFileContent();

        // Assert
        regContent.Should().Contain("Windows Registry Editor Version 5.00");
        regContent.Should().Contain(@"[HKEY_CURRENT_USER\Software\Classes\Directory\shell\LalabQuickBill]");
        regContent.Should().Contain(@"[HKEY_CURRENT_USER\Software\Classes\Directory\shell\LalabQuickBill\command]");
        regContent.Should().Contain(@"[HKEY_CURRENT_USER\Software\Classes\Directory\Background\shell\LalabQuickBill]");
        regContent.Should().Contain(@"[HKEY_CURRENT_USER\Software\Classes\Directory\Background\shell\LalabQuickBill\command]");
        regContent.Should().Contain("⚡ QUICK BILL (Lalab)");
        regContent.Should().Contain("--quick-bill");

        regContent.Should().Contain(@"[HKEY_CURRENT_USER\Software\Classes\Directory\shell\LalabTogglePrinted]");
        regContent.Should().Contain(@"[HKEY_CURRENT_USER\Software\Classes\Directory\shell\LalabTogglePrinted\command]");
        regContent.Should().Contain(@"[HKEY_CURRENT_USER\Software\Classes\Directory\Background\shell\LalabTogglePrinted]");
        regContent.Should().Contain(@"[HKEY_CURRENT_USER\Software\Classes\Directory\Background\shell\LalabTogglePrinted\command]");
        regContent.Should().Contain("🏷️ ĐÃ IN (Lalab)");
        regContent.Should().Contain("--toggle-printed");

        regContent.Should().Contain("%1");
        regContent.Should().Contain("%V");
    }

    [Fact]
    public void RegisterAndUnregisterContextMenu_ShouldCorrectlyUpdateRegistry()
    {
        // Clean up before test
        _service.UnregisterContextMenu();
        _service.IsContextMenuRegistered().Should().BeFalse();

        try
        {
            // Act 1: Register
            _service.RegisterContextMenu();

            // Assert 1: Registered
            _service.IsContextMenuRegistered().Should().BeTrue();

            using (var dirKey = Registry.CurrentUser.OpenSubKey(WindowsContextMenuIntegrationService.QuickBillDirShellKeyPath + @"\command"))
            {
                dirKey.Should().NotBeNull();
                var cmd = dirKey!.GetValue(string.Empty) as string;
                cmd.Should().Contain("--quick-bill");
                cmd.Should().Contain("%1");
            }

            using (var bgKey = Registry.CurrentUser.OpenSubKey(WindowsContextMenuIntegrationService.QuickBillDirBgShellKeyPath + @"\command"))
            {
                bgKey.Should().NotBeNull();
                var cmd = bgKey!.GetValue(string.Empty) as string;
                cmd.Should().Contain("--quick-bill");
                cmd.Should().Contain("%V");
            }

            using (var printDirKey = Registry.CurrentUser.OpenSubKey(WindowsContextMenuIntegrationService.TogglePrintedDirShellKeyPath + @"\command"))
            {
                printDirKey.Should().NotBeNull();
                var cmd = printDirKey!.GetValue(string.Empty) as string;
                cmd.Should().Contain("--toggle-printed");
                cmd.Should().Contain("%1");
            }

            using (var printBgKey = Registry.CurrentUser.OpenSubKey(WindowsContextMenuIntegrationService.TogglePrintedDirBgShellKeyPath + @"\command"))
            {
                printBgKey.Should().NotBeNull();
                var cmd = printBgKey!.GetValue(string.Empty) as string;
                cmd.Should().Contain("--toggle-printed");
                cmd.Should().Contain("%V");
            }

            // Act 2: Unregister
            _service.UnregisterContextMenu();

            // Assert 2: Unregistered
            _service.IsContextMenuRegistered().Should().BeFalse();
        }
        finally
        {
            // Ensure cleanup
            _service.UnregisterContextMenu();
        }
    }

    [Fact]
    public void EnsureContextMenuRegistered_ShouldRegisterContextMenuSuccessfully()
    {
        // Clean up before test
        _service.UnregisterContextMenu();
        _service.IsContextMenuRegistered().Should().BeFalse();

        try
        {
            _service.EnsureContextMenuRegistered();
            _service.IsContextMenuRegistered().Should().BeTrue();
        }
        finally
        {
            _service.UnregisterContextMenu();
        }
    }

    [Fact]
    public void AppSettings_AutoRegisterContextMenu_ShouldDefaultToTrue()
    {
        var settings = new LalabAutoReport.Core.Domain.AppSettings();
        settings.AutoRegisterContextMenu.Should().BeTrue();
    }

    [Fact]
    public async System.Threading.Tasks.Task SqliteSettingsRepository_ShouldPersistAndLoad_AutoRegisterContextMenu()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Lalab_SettingsTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string dbPath = Path.Combine(tempDir, "test_settings.db");

        try
        {
            var connFactory = new LalabAutoReport.Infrastructure.Data.SqliteConnectionFactory(dbPath);
            var migrator = new LalabAutoReport.Infrastructure.Data.DatabaseMigrator(connFactory);
            await migrator.MigrateAsync();

            var repo = new LalabAutoReport.Infrastructure.Data.SqliteSettingsRepository(connFactory);

            // 1. Initial settings should default AutoRegisterContextMenu to true
            var initial = await repo.GetSettingsAsync();
            initial.AutoRegisterContextMenu.Should().BeTrue();

            // 2. Set to false and persist
            initial.AutoRegisterContextMenu = false;
            await repo.SaveSettingsAsync(initial);

            var updated = await repo.GetSettingsAsync();
            updated.AutoRegisterContextMenu.Should().BeFalse();

            // 3. Set back to true and persist
            updated.AutoRegisterContextMenu = true;
            await repo.SaveSettingsAsync(updated);

            var final = await repo.GetSettingsAsync();
            final.AutoRegisterContextMenu.Should().BeTrue();
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task SettingsViewModel_DefaultAutoRegisterContextMenu_ShouldBeTrue()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Lalab_SettingsVmTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string dbPath = Path.Combine(tempDir, "test_settings_vm.db");

        try
        {
            var connFactory = new LalabAutoReport.Infrastructure.Data.SqliteConnectionFactory(dbPath);
            var migrator = new LalabAutoReport.Infrastructure.Data.DatabaseMigrator(connFactory);
            await migrator.MigrateAsync();

            var repo = new LalabAutoReport.Infrastructure.Data.SqliteSettingsRepository(connFactory);
            var fakeBackup = new FakeDatabaseBackupService();
            var fakeCtx = new FakeContextMenuService();

            var vm = new LalabAutoReport.UI.ViewModels.SettingsViewModel(repo, fakeBackup, fakeCtx);

            vm.AutoRegisterContextMenu.Should().BeTrue();
            await vm.LoadSettingsAsync();

            vm.AutoRegisterContextMenu.Should().BeTrue();
            fakeCtx.IsRegistered.Should().BeTrue(); // Should ensure registration since it was false
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task SettingsViewModel_TogglingAutoRegisterContextMenu_ShouldUpdateRegistrationAndStatus()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Lalab_SettingsToggleTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string dbPath = Path.Combine(tempDir, "test_settings_toggle.db");

        try
        {
            var connFactory = new LalabAutoReport.Infrastructure.Data.SqliteConnectionFactory(dbPath);
            var migrator = new LalabAutoReport.Infrastructure.Data.DatabaseMigrator(connFactory);
            await migrator.MigrateAsync();

            var repo = new LalabAutoReport.Infrastructure.Data.SqliteSettingsRepository(connFactory);
            var fakeBackup = new FakeDatabaseBackupService();
            var fakeCtx = new FakeContextMenuService { IsRegistered = true };

            var vm = new LalabAutoReport.UI.ViewModels.SettingsViewModel(repo, fakeBackup, fakeCtx);
            await vm.LoadSettingsAsync();

            // Toggle to false
            vm.AutoRegisterContextMenu = false;
            fakeCtx.IsRegistered.Should().BeFalse();
            vm.IsContextMenuRegistered.Should().BeFalse();
            vm.ContextMenuStatusMessage.Should().Contain("Đã tắt và gỡ bỏ");

            // Toggle to true
            vm.AutoRegisterContextMenu = true;
            fakeCtx.IsRegistered.Should().BeTrue();
            vm.IsContextMenuRegistered.Should().BeTrue();
            vm.ContextMenuStatusMessage.Should().Contain("Đã kích hoạt");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    public void Dispose()
    {
        try
        {
            _service.UnregisterContextMenu();
        }
        catch { }
    }
}

public class FakeDatabaseBackupService : LalabAutoReport.Core.Interfaces.IDatabaseBackupService
{
    public string GetDatabasePath() => "fake_db.db";
    public System.Threading.Tasks.Task<LalabAutoReport.Core.Interfaces.DatabaseBackupInfo> CreateBackupAsync(string? customDestinationPath = null, System.Threading.CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(new LalabAutoReport.Core.Interfaces.DatabaseBackupInfo("fake.bak", "fake.bak", 1024, DateTime.UtcNow));
    public System.Threading.Tasks.Task<IReadOnlyList<LalabAutoReport.Core.Interfaces.DatabaseBackupInfo>> GetBackupsAsync(System.Threading.CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<LalabAutoReport.Core.Interfaces.DatabaseBackupInfo>>(Array.Empty<LalabAutoReport.Core.Interfaces.DatabaseBackupInfo>());
    public System.Threading.Tasks.Task RestoreBackupAsync(string backupFilePath, System.Threading.CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.CompletedTask;
}

public class FakeContextMenuService : LalabAutoReport.Core.Interfaces.IContextMenuIntegrationService
{
    public bool IsRegistered { get; set; }
    public bool IsContextMenuRegistered() => IsRegistered;
    public void RegisterContextMenu() => IsRegistered = true;
    public void UnregisterContextMenu() => IsRegistered = false;
    public void EnsureContextMenuRegistered() => IsRegistered = true;
    public string GenerateRegFileContent() => "Fake Reg Content";
}
