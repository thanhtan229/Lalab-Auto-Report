using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using LalabAutoReport.UI.Web;
using Xunit;

namespace LalabAutoReport.Tests;

public class MobileSpaScriptSyntaxTests
{
    [Fact]
    public void GetIndexHtml_ContainsValidHtmlStructure()
    {
        string html = MobileSpaHtmlProvider.GetIndexHtml();

        html.Should().NotBeNullOrWhiteSpace();
        html.Should().Contain("<!DOCTYPE html>");
        html.Should().Contain("<title>Lalab Mobile</title>");
        html.Should().Contain("<script>");
        html.Should().Contain("</script>");
        html.Should().Contain("id=\"ordersSection\"");
        html.Should().Contain("id=\"reportsSection\"");
        html.Should().Contain("id=\"debtsSection\"");
        html.Should().Contain("id=\"dateToast\"");
        html.Should().Contain("navigateDay(-1)");
        html.Should().Contain("navigateDay(1)");
    }

    [Fact]
    public void GetIndexHtml_JavaScriptBlock_HasBalancedBrackets()
    {
        string html = MobileSpaHtmlProvider.GetIndexHtml();
        var match = Regex.Match(html, @"(?s)<script>(.*?)</script>");
        match.Success.Should().BeTrue("HTML must contain an inline <script> block");

        string script = match.Groups[1].Value;

        int openCurly = CountChar(script, '{');
        int closeCurly = CountChar(script, '}');
        openCurly.Should().Be(closeCurly, $"Curly braces must be balanced (Found {{: {openCurly}, }}: {closeCurly})");

        int openParen = CountChar(script, '(');
        int closeParen = CountChar(script, ')');
        openParen.Should().Be(closeParen, $"Parentheses must be balanced (Found (: {openParen}, ): {closeParen})");

        int openSquare = CountChar(script, '[');
        int closeSquare = CountChar(script, ']');
        openSquare.Should().Be(closeSquare, $"Square brackets must be balanced (Found [: {openSquare}, ]: {closeSquare})");
    }

    [Fact]
    public void GetIndexHtml_ContainsAllEssentialGlobalFunctions()
    {
        string html = MobileSpaHtmlProvider.GetIndexHtml();
        var match = Regex.Match(html, @"(?s)<script>(.*?)</script>");
        string script = match.Groups[1].Value;

        var requiredFunctions = new[]
        {
            "checkAuthAndLoad",
            "submitPin",
            "logout",
            "setToday",
            "setThisMonth",
            "loadOrders",
            "renderOrders",
            "setFilterMode",
            "filterOrders",
            "navigateDay",
            "showDateToast",
            "setupSwipeGestures",
            "viewBill",
            "viewLabel",
            "viewOrderLabel",
            "printOrderLabel",
            "printBillLabel",
            "showBillQr",
            "closePreviewModal",
            "switchTab",
            "loadMonthlyReport",
            "loadDebts"
        };

        foreach (var fn in requiredFunctions)
        {
            script.Should().Contain($"function {fn}", $"Function '{fn}' must be defined in the mobile script");
        }
    }

    [Fact]
    public void GetIndexHtml_PrintedStatusIsReadOnly_AndHeaderDoesNotContainDeliveryBadgeBelowPrintedStatus()
    {
        string html = MobileSpaHtmlProvider.GetIndexHtml();

        // Print status badge must be display-only, not a clickable button that toggles state
        html.Should().NotContain("togglePrinted(${o.id})", "Đã in/Chờ in badge must not be clickable to toggle state");
        html.Should().Contain("badge-printed", "Should still show printed badge class");
        html.Should().Contain("badge-pending", "Should still show pending badge class");

        // Delivery badge under printed status must be removed
        html.Should().NotContain("${deliveryBadge}", "Delivery badge under printed status header must be removed");

        // Action button at the bottom for delivery toggle must still exist
        html.Should().Contain("toggleDelivered(${o.id})", "Bottom action buttons should still allow toggling delivery");
    }

    [Fact]
    public void GetIndexHtml_JavaScriptSyntax_PassesNodeCheck()
    {
        string html = MobileSpaHtmlProvider.GetIndexHtml();
        var match = Regex.Match(html, @"(?s)<script>(.*?)</script>");
        string script = match.Groups[1].Value;

        string tempJsPath = Path.Combine(Path.GetTempPath(), $"lalab_mobile_test_{Guid.NewGuid():N}.js");
        try
        {
            File.WriteAllText(tempJsPath, script);

            var psi = new ProcessStartInfo
            {
                FileName = "node",
                Arguments = $"--check \"{tempJsPath}\"",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                string err = proc.StandardError.ReadToEnd();
                proc.WaitForExit(5000);
                proc.ExitCode.Should().Be(0, $"Node.js syntax validation failed: {err}");
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Node.js is optional in some environments; test gracefully skips external process check
        }
        finally
        {
            if (File.Exists(tempJsPath))
            {
                try { File.Delete(tempJsPath); } catch { }
            }
        }
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Staff")]
    public void ActualLanScript_SuccessfulLoginClosesPinModalAndLoadsOrders(string role)
    {
        string script = Regex.Match(MobileSpaHtmlProvider.GetIndexHtml(), @"(?s)<script>(.*?)</script>").Groups[1].Value;
        string fixture = @"
const assert = require('node:assert/strict');
const nodes = new Map();
global.window = {addEventListener(){}};
global.localStorage = {getItem(){return 'fixture-token'}};
global.document = {getElementById(id){if(!nodes.has(id)) nodes.set(id,{value:'',innerText:'',innerHTML:'',classList:{hidden:false,add(){this.hidden=true},remove(){this.hidden=false}},focus(){},addEventListener(){}});return nodes.get(id)}};
global.fetch = async () => ({ok:true,json:async()=>({role:ROLE,workshopName:'Fixture Workshop'})});
".Replace("ROLE", System.Text.Json.JsonSerializer.Serialize(role));
        string assertions = @"
let didLoad = false; loadOrders = () => {didLoad=true};
checkAuthAndLoad().then(()=>{assert.equal(nodes.get('pinModal').classList.hidden,true); assert.equal(didLoad,true); assert.match(nodes.get('roleBadge').innerHTML,/LAN/); assert.equal(nodes.get('workshopTitle').innerText,'Fixture Workshop')}).catch(e=>{console.error(e);process.exitCode=1});
";
        string path = Path.Combine(Path.GetTempPath(), "lalab_login_" + Guid.NewGuid().ToString("N") + ".cjs");
        try
        {
            File.WriteAllText(path, fixture + script + assertions);
            using var process = Process.Start(new ProcessStartInfo("node", $"\"{path}\"") { RedirectStandardError=true,RedirectStandardOutput=true,UseShellExecute=false,CreateNoWindow=true })!;
            string error = process.StandardError.ReadToEnd(); process.WaitForExit(5000);
            Assert.True(process.ExitCode == 0,error);
        }
        finally { File.Delete(path); }
    }

    private static int CountChar(string text, char target)
    {
        int count = 0;
        foreach (char c in text)
        {
            if (c == target) count++;
        }
        return count;
    }
}
