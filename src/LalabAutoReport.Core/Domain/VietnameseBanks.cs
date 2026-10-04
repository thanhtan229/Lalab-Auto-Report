using System;
using System.Collections.Generic;
using System.Linq;

namespace LalabAutoReport.Core.Domain;

/// <summary>
/// Thông tin định danh ngân hàng Việt Nam hỗ trợ chuẩn Napas247 / VietQR
/// </summary>
public record BankInfo(string Bin, string ShortName, string FullName)
{
    /// <summary>
    /// Chuỗi thể hiện trên dropdown: "Tên Ngân Hàng (Tên Viết Tắt) - Tên Đầy Đủ"
    /// </summary>
    public string DisplayName => $"{ShortName} - {FullName}";

    public override string ToString() => DisplayName;
}

public static class VietnameseBanks
{
    public static readonly IReadOnlyList<BankInfo> All = new List<BankInfo>
    {
        new("970422", "MB Bank (MB)", "Ngân hàng TMCP Quân Đội"),
        new("970436", "Vietcombank (VCB)", "Ngân hàng TMCP Ngoại Thương"),
        new("970407", "Techcombank (TCB)", "Ngân hàng TMCP Kỹ Thương"),
        new("970415", "VietinBank (CTG)", "Ngân hàng TMCP Công Thương"),
        new("970418", "BIDV", "Ngân hàng TMCP Đầu Tư và Phát Triển"),
        new("970416", "ACB", "Ngân hàng TMCP Á Châu"),
        new("970432", "VPBank (VPB)", "Ngân hàng TMCP Việt Nam Thịnh Vượng"),
        new("970423", "TPBank (TPB)", "Ngân hàng TMCP Tiên Phong"),
        new("970405", "Agribank (VBA)", "Ngân hàng Nông Nghiệp & PTNT"),
        new("970403", "Sacombank (STB)", "Ngân hàng TMCP Sài Gòn Thương Tín"),
        new("970441", "VIB", "Ngân hàng TMCP Quốc Tế"),
        new("970426", "MSB", "Ngân hàng TMCP Hàng Hải"),
        new("970443", "SHB", "Ngân hàng TMCP Sài Gòn - Hà Nội"),
        new("970437", "HDBank (HDB)", "Ngân hàng TMCP Phát Triển TP.HCM"),
        new("970448", "OCB", "Ngân hàng TMCP Phương Đông"),
        new("970449", "LPBank (LPB)", "Ngân hàng TMCP Lộc Phát Việt Nam"),
        new("970440", "SeABank (SEAB)", "Ngân hàng TMCP Đông Nam Á"),
        new("970431", "Eximbank (EIB)", "Ngân hàng TMCP Xuất Nhập Khẩu"),
        new("970409", "Bac A Bank (BAB)", "Ngân hàng TMCP Bắc Á"),
        new("970438", "BaoViet Bank (BVB)", "Ngân hàng TMCP Bảo Việt"),
        new("970412", "PVcomBank (PVCB)", "Ngân hàng TMCP Đại Chúng"),
        new("970454", "BVBank (VCCB)", "Ngân hàng TMCP Bản Việt"),
        new("970428", "Nam A Bank (NAB)", "Ngân hàng TMCP Nam Á"),
        new("970400", "Saigonbank (SGB)", "Ngân hàng TMCP Sài Gòn Công Thương"),
        new("970427", "VietABank (VAB)", "Ngân hàng TMCP Việt Á"),
        new("970419", "NCB", "Ngân hàng TMCP Quốc Dân"),
        new("970452", "Kienlongbank (KLB)", "Ngân hàng TMCP Kiên Long"),
        new("970430", "PGBank (PGB)", "Ngân hàng TMCP Thịnh Vượng & PT"),
        new("970433", "VietBank", "Ngân hàng TMCP Việt Nam Thương Tín"),
        new("546034", "Cake by VPBank (CAKE)", "Ngân hàng số Cake"),
        new("546035", "Ubank by VPBank", "Ngân hàng số Ubank"),
        new("963388", "Timo by BVBank", "Ngân hàng số Timo"),
        new("970424", "Shinhan Bank", "Ngân hàng TNHH MTV Shinhan Việt Nam"),
        new("970457", "Woori Bank", "Ngân hàng Woori Việt Nam"),
        new("970408", "GPBank", "Ngân hàng TM TNHH MTV Dầu Khí Toàn Cầu"),
        new("970414", "OceanBank", "Ngân hàng TM TNHH MTV Đại Dương")
    };

    public static BankInfo? FindByBin(string? bin)
    {
        if (string.IsNullOrWhiteSpace(bin)) return null;
        string clean = bin.Trim();
        return All.FirstOrDefault(b => b.Bin.Equals(clean, StringComparison.OrdinalIgnoreCase));
    }

    public static string GetShortNameOrBin(string? bin)
    {
        if (string.IsNullOrWhiteSpace(bin)) return string.Empty;
        var info = FindByBin(bin);
        return info != null ? info.ShortName : bin;
    }
}
