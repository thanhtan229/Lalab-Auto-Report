using System;
using FluentAssertions;
using LalabAutoReport.Infrastructure.Reporting;
using Xunit;

namespace LalabAutoReport.Tests;

public class VietQrGeneratorTests
{
    [Fact]
    public void BuildVietQrPayload_WithValidInputs_GeneratesValidEmvCoString()
    {
        string bankBin = "970422"; // MB Bank
        string account = "0123456789";
        long amount = 150000;
        string memo = "BILL-20260930-0001";

        string payload = VietQrGenerator.BuildVietQrPayload(bankBin, account, amount, memo);

        payload.Should().NotBeNullOrEmpty();
        payload.Should().StartWith("000201010212"); // Format indicator + Dynamic QR
        payload.Should().Contain("A000000727"); // Napas AID
        payload.Should().Contain(bankBin);
        payload.Should().Contain(account);
        payload.Should().Contain("QRIBFTTA");
        payload.Should().Contain("5303704"); // VND
        payload.Should().Contain("5406150000"); // Amount 150000
        payload.Should().Contain("5802VN"); // Country code
        payload.Should().Contain("6304"); // CRC tag
        payload.Length.Should().BeGreaterThan(60);
    }

    [Fact]
    public void BuildVietQrPayload_WithZeroAmount_GeneratesStaticQrWithoutAmountTag()
    {
        string payload = VietQrGenerator.BuildVietQrPayload("970436", "987654321", 0, "BILL-002");

        payload.Should().StartWith("000201010211"); // Static QR
        payload.Should().NotContain("530370454");
    }

    [Fact]
    public void BuildVietQrPayload_WithMissingBankOrAccount_ReturnsEmpty()
    {
        VietQrGenerator.BuildVietQrPayload("", "123456", 100000, "MEMO").Should().BeEmpty();
        VietQrGenerator.BuildVietQrPayload("970422", "", 100000, "MEMO").Should().BeEmpty();
    }

    [Fact]
    public void SanitizeMemo_RemovesDiacriticsAndSpecialCharacters()
    {
        string input = "Hóa Đơn Anh Văn An #123!";
        string sanitized = VietQrGenerator.SanitizeMemo(input);

        sanitized.Should().Be("Hoa Don Anh Van An 123");
    }

    [Fact]
    public void ComputeCrc16Ccitt_ReturnsFourHexCharacters()
    {
        string data = "00020101021238540010A0000007276304";
        string crc = VietQrGenerator.ComputeCrc16Ccitt(data);
        crc.Should().HaveLength(4);
        crc.Should().MatchRegex("^[0-9A-F]{4}$");
    }

    [Fact]
    public void VietnameseBanks_All_ContainsMajorBanksWithAcronymAndName()
    {
        var banks = LalabAutoReport.Core.Domain.VietnameseBanks.All;
        banks.Should().NotBeNullOrEmpty();
        banks.Count.Should().BeGreaterThanOrEqualTo(30);

        var mb = banks.FirstOrDefault(b => b.Bin == "970422");
        mb.Should().NotBeNull();
        mb!.ShortName.Should().Be("MB Bank (MB)");
        mb.DisplayName.Should().Contain("MB Bank (MB)");
        mb.DisplayName.Should().Contain("Quân Đội");

        var vcb = banks.FirstOrDefault(b => b.Bin == "970436");
        vcb.Should().NotBeNull();
        vcb!.ShortName.Should().Be("Vietcombank (VCB)");

        var tcb = banks.FirstOrDefault(b => b.Bin == "970407");
        tcb.Should().NotBeNull();
        tcb!.ShortName.Should().Be("Techcombank (TCB)");
    }

    [Fact]
    public void VietnameseBanks_FindByBin_And_GetShortNameOrBin_WorkCorrectly()
    {
        var bank = LalabAutoReport.Core.Domain.VietnameseBanks.FindByBin("970422");
        bank.Should().NotBeNull();
        bank!.ShortName.Should().Be("MB Bank (MB)");

        var trimmedBank = LalabAutoReport.Core.Domain.VietnameseBanks.FindByBin("  970436  ");
        trimmedBank.Should().NotBeNull();
        trimmedBank!.ShortName.Should().Be("Vietcombank (VCB)");

        LalabAutoReport.Core.Domain.VietnameseBanks.FindByBin("999999").Should().BeNull();
        LalabAutoReport.Core.Domain.VietnameseBanks.FindByBin("").Should().BeNull();

        LalabAutoReport.Core.Domain.VietnameseBanks.GetShortNameOrBin("970422").Should().Be("MB Bank (MB)");
        LalabAutoReport.Core.Domain.VietnameseBanks.GetShortNameOrBin("CUSTOM_CODE").Should().Be("CUSTOM_CODE");
        LalabAutoReport.Core.Domain.VietnameseBanks.GetShortNameOrBin("").Should().BeEmpty();
    }
}
