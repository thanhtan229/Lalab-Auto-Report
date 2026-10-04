using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace LalabAutoReport.Tests;

public class XamlResourceIntegrityTests
{
    private static string GetUiProjectDirectory()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string? candidate = Path.GetFullPath(Path.Combine(baseDir, "../../../../src/LalabAutoReport.UI"));
        if (Directory.Exists(candidate))
        {
            return candidate;
        }

        candidate = Path.GetFullPath(Path.Combine(baseDir, "../../../../../src/LalabAutoReport.UI"));
        if (Directory.Exists(candidate))
        {
            return candidate;
        }

        throw new DirectoryNotFoundException($"Could not locate LalabAutoReport.UI directory from {baseDir}");
    }

    [Fact]
    public void TinixStyles_ContainsTextBoxAndStandardStyles()
    {
        var uiDir = GetUiProjectDirectory();
        var stylesXaml = Path.Combine(uiDir, "Resources", "TinixStyles.xaml");
        File.Exists(stylesXaml).Should().BeTrue();

        var content = File.ReadAllText(stylesXaml);
        content.Should().Contain("x:Key=\"Tinix.TextBox.Standard\"");
        content.Should().Contain("x:Key=\"Tinix.TextBox\"");
    }

    [Fact]
    public void SettingsView_DoesNotContainUnresolvedTinixResources()
    {
        var uiDir = GetUiProjectDirectory();
        var settingsXaml = Path.Combine(uiDir, "Views", "SettingsView.xaml");
        File.Exists(settingsXaml).Should().BeTrue();

        var definedKeys = GetGlobalDefinedResourceKeys(uiDir);

        var content = File.ReadAllText(settingsXaml);
        var matches = Regex.Matches(content, @"StaticResource\s+([A-Za-z0-9_\.]+)");

        var missing = new List<string>();
        foreach (Match match in matches)
        {
            var key = match.Groups[1].Value;
            if (key.StartsWith("Tinix.") && !definedKeys.Contains(key))
            {
                missing.Add(key);
            }
        }

        missing.Should().BeEmpty("all Tinix resources referenced in SettingsView.xaml must be defined");
    }

    [Fact]
    public void AllXamlViews_HaveAllTinixStaticResourcesDefined()
    {
        var uiDir = GetUiProjectDirectory();
        var definedKeys = GetGlobalDefinedResourceKeys(uiDir);

        var xamlFiles = Directory.GetFiles(uiDir, "*.xaml", SearchOption.AllDirectories);
        var failures = new List<string>();

        foreach (var file in xamlFiles)
        {
            var fileName = Path.GetFileName(file);
            var content = File.ReadAllText(file);
            var matches = Regex.Matches(content, @"StaticResource\s+([A-Za-z0-9_\.]+)");

            foreach (Match match in matches)
            {
                var key = match.Groups[1].Value;
                if (key.StartsWith("Tinix.") && !definedKeys.Contains(key))
                {
                    failures.Add($"{fileName} references undefined '{key}'");
                }
            }
        }

        failures.Distinct().Should().BeEmpty("no XAML files should reference undefined Tinix static resources");
    }

    private static HashSet<string> GetGlobalDefinedResourceKeys(string uiDir)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var resourceFiles = Directory.GetFiles(Path.Combine(uiDir, "Resources"), "*.xaml", SearchOption.TopDirectoryOnly);

        foreach (var file in resourceFiles)
        {
            var content = File.ReadAllText(file);
            var matches = Regex.Matches(content, @"x:Key=""([^""]+)""");
            foreach (Match match in matches)
            {
                keys.Add(match.Groups[1].Value);
            }
        }

        return keys;
    }

    [Fact]
    public void PaymentToggle_ConfiguredAsCheckboxStyle()
    {
        var uiDir = GetUiProjectDirectory();
        var stylesXaml = Path.Combine(uiDir, "Resources", "TinixStyles.xaml");
        File.Exists(stylesXaml).Should().BeTrue();

        var content = File.ReadAllText(stylesXaml);
        content.Should().Contain("x:Key=\"Tinix.Button.PaymentToggle\"");

        // Checkbox icon should be Collapsed by default (unpaid = empty checkbox)
        content.Should().Contain("x:Name=\"checkIcon\"");
        
        // When IsPaid == True, checkIcon must be set to Visible
        content.Should().Contain("<DataTrigger Binding=\"{Binding IsPaid}\" Value=\"True\">");
        content.Should().Contain("<Setter TargetName=\"checkIcon\" Property=\"Visibility\" Value=\"Visible\"/>");
    }
}
