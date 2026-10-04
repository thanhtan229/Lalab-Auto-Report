using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;
using LalabAutoReport.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LalabAutoReport.UI.Services;

/// <summary>
/// Manages the Windows System Tray notification icon, context menu, and background daemon lifecycle.
/// </summary>
public class SystemTrayManager : ISystemTrayManager, IDisposable
{
    private readonly ILogger<SystemTrayManager>? _logger;
    private Forms.NotifyIcon? _notifyIcon;
    private Forms.ContextMenuStrip? _contextMenu;
    private Forms.ToolStripMenuItem? _openAppMenuItem;
    private Forms.ToolStripMenuItem? _openWebMenuItem;
    private Forms.ToolStripMenuItem? _statusMenuItem;
    private Forms.ToolStripMenuItem? _exitMenuItem;

    private bool _hasShownBalloonNotification = false;
    private string? _remoteUrl;
    private bool _isDisposed = false;
    private readonly object _lock = new();

    public bool IsVisible => _notifyIcon?.Visible ?? false;

    public SystemTrayManager(ILogger<SystemTrayManager>? logger = null)
    {
        _logger = logger;
    }

    public void Initialize()
    {
        lock (_lock)
        {
            if (_notifyIcon != null) return;

            try
            {
                Drawing.Icon? icon = LoadApplicationIcon();

                _contextMenu = new Forms.ContextMenuStrip();

                _openAppMenuItem = new Forms.ToolStripMenuItem("👁️ Mở Lalab Auto Report")
                {
                    Font = new Drawing.Font(_contextMenu.Font, Drawing.FontStyle.Bold)
                };
                _openAppMenuItem.Click += (s, e) => RestoreMainWindow();
                _contextMenu.Items.Add(_openAppMenuItem);

                _openWebMenuItem = new Forms.ToolStripMenuItem("🌐 Mở Web Quản Lý (Trình duyệt)");
                _openWebMenuItem.Click += (s, e) => OpenWebInBrowser();
                _contextMenu.Items.Add(_openWebMenuItem);

                _statusMenuItem = new Forms.ToolStripMenuItem("ℹ️ Web Server: Đang hoạt động")
                {
                    Enabled = false
                };
                _contextMenu.Items.Add(_statusMenuItem);

                _contextMenu.Items.Add(new Forms.ToolStripSeparator());

                _exitMenuItem = new Forms.ToolStripMenuItem("❌ Thoát hoàn toàn ứng dụng");
                _exitMenuItem.Click += (s, e) => ExitApplication();
                _contextMenu.Items.Add(_exitMenuItem);

                _notifyIcon = new Forms.NotifyIcon
                {
                    Icon = icon,
                    Text = "Lalab Auto Report - Web Server Đang Chạy",
                    ContextMenuStrip = _contextMenu,
                    Visible = true
                };

                _notifyIcon.DoubleClick += (s, e) => RestoreMainWindow();
                _notifyIcon.BalloonTipClicked += (s, e) => RestoreMainWindow();

                _logger?.LogInformation("System Tray Icon initialized successfully.");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to initialize System Tray Icon.");
            }
        }
    }

    private Drawing.Icon LoadApplicationIcon()
    {
        try
        {
            string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "app.ico");
            if (File.Exists(icoPath))
            {
                return new Drawing.Icon(icoPath);
            }
        }
        catch { }

        try
        {
            string? exePath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
            {
                var exeIcon = Drawing.Icon.ExtractAssociatedIcon(exePath);
                if (exeIcon != null) return exeIcon;
            }
        }
        catch { }

        return Drawing.SystemIcons.Application;
    }

    public void ShowFirstTimeMinimizedNotification()
    {
        if (_hasShownBalloonNotification || _notifyIcon == null || !_notifyIcon.Visible) return;

        _hasShownBalloonNotification = true;
        try
        {
            _notifyIcon.ShowBalloonTip(
                3500,
                "Lalab Auto Report",
                "Ứng dụng vẫn đang chạy ngầm dưới khay hệ thống để phục vụ Web di động. Nhấp đúp vào đây để mở lại.",
                Forms.ToolTipIcon.Info
            );
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to show system tray balloon tip.");
        }
    }

    public void UpdateStatus(string statusText)
    {
        if (_statusMenuItem != null)
        {
            _statusMenuItem.Text = $"ℹ️ {statusText}";
        }

        if (_notifyIcon != null)
        {
            string tooltip = $"Lalab Auto Report: {statusText}";
            if (tooltip.Length > 63)
            {
                tooltip = tooltip.Substring(0, 60) + "...";
            }
            _notifyIcon.Text = tooltip;
        }
    }

    public void UpdateRemoteUrl(string? remoteUrl)
    {
        _remoteUrl = remoteUrl;
    }

    private void RestoreMainWindow()
    {
        try
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var mainWindow = System.Windows.Application.Current.MainWindow;
                if (mainWindow is MainWindow mw)
                {
                    mw.RestoreAndActivate();
                }
                else if (mainWindow != null)
                {
                    if (mainWindow.WindowState == WindowState.Minimized)
                    {
                        mainWindow.WindowState = WindowState.Normal;
                    }
                    mainWindow.Show();
                    mainWindow.Activate();
                    mainWindow.Focus();
                }
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to restore main window from system tray.");
        }
    }

    private void OpenWebInBrowser()
    {
        try
        {
            string targetUrl = !string.IsNullOrWhiteSpace(_remoteUrl) ? _remoteUrl : "http://localhost:5050";
            Process.Start(new ProcessStartInfo
            {
                FileName = targetUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to open web URL from system tray.");
        }
    }

    private void ExitApplication()
    {
        try
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var mainWindow = System.Windows.Application.Current.MainWindow;
                if (mainWindow is MainWindow mw)
                {
                    mw.RequestExplicitExit();
                }
                else
                {
                    Dispose();
                    System.Windows.Application.Current.Shutdown();
                }
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to exit application from system tray.");
            Environment.Exit(0);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed) return;
            _isDisposed = true;

            try
            {
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                    _notifyIcon = null;
                }

                if (_contextMenu != null)
                {
                    _contextMenu.Dispose();
                    _contextMenu = null;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Exception during SystemTrayManager disposal.");
            }
        }
    }
}
