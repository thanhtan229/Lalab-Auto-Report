using System;
using System.Text;
using System.Windows;
using System.Windows.Media;
using QRCoder;

namespace LalabAutoReport.Infrastructure.Reporting;

/// <summary>
/// Service to generate offline EMVCo-compliant VietQR payloads and render them to WPF DrawingContext
/// </summary>
public static class VietQrGenerator
{
    /// <summary>
    /// Formats a TLV (Tag-Length-Value) block per EMVCo standard
    /// </summary>
    public static string FormatTlv(string tag, string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        int byteLength = Encoding.UTF8.GetByteCount(value);
        return $"{tag}{byteLength:D2}{value}";
    }

    /// <summary>
    /// Builds a full EMVCo-compliant Napas247 VietQR payload string
    /// </summary>
    public static string BuildVietQrPayload(string bankBin, string accountNumber, long amount, string? memo)
    {
        if (string.IsNullOrWhiteSpace(bankBin) || string.IsNullOrWhiteSpace(accountNumber))
        {
            return string.Empty;
        }

        var sb = new StringBuilder();

        // Tag 00: Payload Format Indicator (01)
        sb.Append(FormatTlv("00", "01"));

        // Tag 01: Point of Initiation Method (12 = Dynamic with Amount, 11 = Static)
        sb.Append(FormatTlv("01", amount > 0 ? "12" : "11"));

        // Tag 38: Merchant Account Information (Consumer-Presented Napas Transfer)
        var sub38 = new StringBuilder();
        // 00: Napas GUID
        sub38.Append(FormatTlv("00", "A000000727"));
        // 01: Beneficiary Bank BIN + Account
        var sub01 = new StringBuilder();
        sub01.Append(FormatTlv("00", bankBin.Trim()));
        sub01.Append(FormatTlv("01", accountNumber.Trim()));
        sub38.Append(FormatTlv("01", sub01.ToString()));
        // 02: Service Code (QRIBFTTA = Transfer to Account)
        sub38.Append(FormatTlv("02", "QRIBFTTA"));

        sb.Append(FormatTlv("38", sub38.ToString()));

        // Tag 53: Transaction Currency (704 = VND)
        sb.Append(FormatTlv("53", "704"));

        // Tag 54: Transaction Amount
        if (amount > 0)
        {
            sb.Append(FormatTlv("54", amount.ToString()));
        }

        // Tag 58: Country Code (VN)
        sb.Append(FormatTlv("58", "VN"));

        // Tag 62: Additional Data Field Template (Purpose / Order Reference)
        if (!string.IsNullOrWhiteSpace(memo))
        {
            string sanitizedMemo = SanitizeMemo(memo);
            if (!string.IsNullOrWhiteSpace(sanitizedMemo))
            {
                var sub62 = FormatTlv("08", sanitizedMemo);
                sb.Append(FormatTlv("62", sub62));
            }
        }

        // Tag 63: CRC16 checksum
        sb.Append("6304");
        string crc = ComputeCrc16Ccitt(sb.ToString());
        sb.Append(crc);

        return sb.ToString();
    }

    /// <summary>
    /// Computes CRC16-CCITT (polynomial 0x1021, init 0xFFFF)
    /// </summary>
    public static string ComputeCrc16Ccitt(string data)
    {
        ushort crc = 0xFFFF;
        byte[] bytes = Encoding.ASCII.GetBytes(data);

        foreach (byte b in bytes)
        {
            crc ^= (ushort)(b << 8);
            for (int i = 0; i < 8; i++)
            {
                if ((crc & 0x8000) != 0)
                {
                    crc = (ushort)((crc << 1) ^ 0x1021);
                }
                else
                {
                    crc <<= 1;
                }
            }
        }

        return crc.ToString("X4");
    }

    /// <summary>
    /// Removes diacritics and special characters from memo for banking compatibility
    /// </summary>
    public static string SanitizeMemo(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        string noDiacritics = Core.Services.CustomerNormalizer.RemoveDiacritics(text);
        var sb = new StringBuilder();

        foreach (char c in noDiacritics)
        {
            if (char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_')
            {
                sb.Append(c);
            }
        }

        string result = sb.ToString().Trim();
        // Limit to 25 chars for bank transfer memo compatibility
        return result.Length > 25 ? result.Substring(0, 25) : result;
    }

    /// <summary>
    /// Renders a VietQR code directly onto a WPF DrawingContext with crisp vector precision
    /// </summary>
    public static void RenderQrCode(DrawingContext dc, Rect bounds, string payload, Brush? foreground = null, Brush? background = null)
    {
        if (string.IsNullOrWhiteSpace(payload)) return;

        foreground ??= Brushes.Black;
        background ??= Brushes.White;

        // Draw background
        dc.DrawRectangle(background, null, bounds);

        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);

        var matrix = qrCodeData.ModuleMatrix;
        int count = matrix.Count;
        if (count == 0) return;

        double size = Math.Min(bounds.Width, bounds.Height);
        double moduleSize = size / count;
        double startX = bounds.X + (bounds.Width - size) / 2;
        double startY = bounds.Y + (bounds.Height - size) / 2;

        for (int row = 0; row < count; row++)
        {
            for (int col = 0; col < count; col++)
            {
                if (matrix[row][col])
                {
                    dc.DrawRectangle(foreground, null, new Rect(
                        startX + col * moduleSize,
                        startY + row * moduleSize,
                        moduleSize + 0.3, // Slight overlap to avoid rendering seams
                        moduleSize + 0.3
                    ));
                }
            }
        }
    }
}
