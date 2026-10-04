using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LalabAutoReport.Infrastructure.Reporting;

public class ClosedXmlBillExporter : IExcelBillExporter
{
    private readonly ISettingsRepository _settingsRepository;
    private readonly ILogger<ClosedXmlBillExporter>? _logger;
    private static readonly CultureInfo ViCulture = new("vi-VN");

    public ClosedXmlBillExporter(ISettingsRepository settingsRepository, ILogger<ClosedXmlBillExporter>? logger = null)
    {
        _settingsRepository = settingsRepository;
        _logger = logger;
    }

    public async Task<string> ExportBillToExcelAsync(CustomerBill bill, string? destinationDirectory = null, CancellationToken cancellationToken = default)
    {
        var settings = await _settingsRepository.GetSettingsAsync(cancellationToken);
        string exportDir = destinationDirectory ?? string.Empty;
        if (string.IsNullOrWhiteSpace(exportDir))
        {
            exportDir = settings.BillExportFolder;
        }

        if (string.IsNullOrWhiteSpace(exportDir))
        {
            exportDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LalabReports", "Bills");
        }

        Directory.CreateDirectory(exportDir);

        string fileName = GenerateDeterministicFileName(bill);
        string filePath = Path.Combine(exportDir, fileName);

        GenerateWorkbook(bill, filePath, settings);

        _logger?.LogInformation("Successfully exported bill {BillNumber} to Excel: {FilePath}", bill.BillNumber, filePath);

        return filePath;
    }

    public static string GenerateDeterministicFileName(CustomerBill bill)
    {
        string slug = WpfJpegBillExporter.SanitizeSlug(bill.CustomerNameSnapshot);
        return $"{bill.BillNumber}_{slug}.xlsx";
    }

    private static void GenerateWorkbook(CustomerBill bill, string destinationFilePath, AppSettings? settings = null)
    {
        using var workbook = new XLWorkbook();
        
        string sheetName = string.IsNullOrWhiteSpace(bill.BillNumber) ? "Hóa đơn" : bill.BillNumber.Replace("/", "-");
        if (sheetName.Length > 30) sheetName = sheetName.Substring(0, 30);
        
        var ws = workbook.Worksheets.Add(sheetName);
        ws.ShowGridLines = true;

        // Palette
        var brandBlue = XLColor.FromArgb(26, 86, 219);      // #1A56DB
        var subtleBg = XLColor.FromArgb(248, 250, 252);     // #F8FAFC
        var headerBg = XLColor.FromArgb(241, 245, 249);     // #F1F5F9
        var highlightBlue = XLColor.FromArgb(239, 246, 255); // #EFF6FF
        var borderLight = XLColor.FromArgb(226, 232, 240);   // #E2E8F0
        var textPrimary = XLColor.FromArgb(15, 23, 42);      // #0F172A
        var textSecondary = XLColor.FromArgb(71, 85, 105);   // #475569

        string fontName = "Segoe UI";

        string wsName = !string.IsNullOrWhiteSpace(settings?.WorkshopName) 
            ? settings.WorkshopName.ToUpperInvariant() 
            : "XƯỞNG IN ẢNH CHUYÊN NGHIỆP";
        string wsSlogan = !string.IsNullOrWhiteSpace(settings?.WorkshopSlogan) 
            ? settings.WorkshopSlogan 
            : string.Empty;
        string headerBrand = !string.IsNullOrWhiteSpace(wsSlogan) ? $"{wsName} — {wsSlogan}" : wsName;

        // Row 1: Brand Accent Header
        ws.Range("A1:I1").Merge();
        ws.Cell("A1").Value = headerBrand;
        ws.Cell("A1").Style.Font.FontName = fontName;
        ws.Cell("A1").Style.Font.FontSize = 11;
        ws.Cell("A1").Style.Font.Bold = true;
        ws.Cell("A1").Style.Font.FontColor = brandBlue;
        ws.Row(1).Height = 22;

        // Row 2: Title
        ws.Range("A2:I2").Merge();
        ws.Cell("A2").Value = "HÓA ĐƠN BÁN HÀNG";
        ws.Cell("A2").Style.Font.FontName = fontName;
        ws.Cell("A2").Style.Font.FontSize = 18;
        ws.Cell("A2").Style.Font.Bold = true;
        ws.Cell("A2").Style.Font.FontColor = textPrimary;
        ws.Cell("A2").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Row(2).Height = 32;

        // Row 3: Spacer
        ws.Row(3).Height = 10;

        // Row 4-5: Info Card
        ws.Cell("A4").Value = "Mã hóa đơn:";
        ws.Cell("A4").Style.Font.FontName = fontName;
        ws.Cell("A4").Style.Font.Bold = true;
        ws.Cell("A4").Style.Font.FontColor = textSecondary;

        ws.Range("B4:D4").Merge();
        ws.Cell("B4").Value = bill.BillNumber;
        ws.Cell("B4").Style.Font.FontName = fontName;
        ws.Cell("B4").Style.Font.Bold = true;
        ws.Cell("B4").Style.Font.FontColor = brandBlue;

        ws.Cell("E4").Value = "Kỳ bill:";
        ws.Cell("E4").Style.Font.FontName = fontName;
        ws.Cell("E4").Style.Font.Bold = true;
        ws.Cell("E4").Style.Font.FontColor = textSecondary;

        ws.Range("F4:I4").Merge();
        ws.Cell("F4").Value = $"{FormatDate(bill.PeriodStart)} → {FormatDate(bill.PeriodEnd)}";
        ws.Cell("F4").Style.Font.FontName = fontName;
        ws.Cell("F4").Style.Font.FontColor = textPrimary;

        ws.Cell("A5").Value = "Khách hàng:";
        ws.Cell("A5").Style.Font.FontName = fontName;
        ws.Cell("A5").Style.Font.Bold = true;
        ws.Cell("A5").Style.Font.FontColor = textSecondary;

        string phoneStr = !string.IsNullOrWhiteSpace(bill.PhoneSnapshot) ? $" ({bill.PhoneSnapshot})" : "";
        ws.Range("B5:D5").Merge();
        ws.Cell("B5").Value = $"{bill.CustomerNameSnapshot}{phoneStr}";
        ws.Cell("B5").Style.Font.FontName = fontName;
        ws.Cell("B5").Style.Font.Bold = true;
        ws.Cell("B5").Style.Font.FontColor = textPrimary;

        ws.Cell("E5").Value = "Ngày xuất:";
        ws.Cell("E5").Style.Font.FontName = fontName;
        ws.Cell("E5").Style.Font.Bold = true;
        ws.Cell("E5").Style.Font.FontColor = textSecondary;

        ws.Range("F5:I5").Merge();
        ws.Cell("F5").Value = bill.CreatedAt.ToString("dd/MM/yyyy HH:mm");
        ws.Cell("F5").Style.Font.FontName = fontName;
        ws.Cell("F5").Style.Font.FontColor = textPrimary;

        var infoCard = ws.Range("A4:I5");
        infoCard.Style.Fill.BackgroundColor = subtleBg;
        infoCard.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        infoCard.Style.Border.OutsideBorderColor = borderLight;
        ws.Row(4).Height = 22;
        ws.Row(5).Height = 22;

        // Row 6: Spacer
        ws.Row(6).Height = 12;

        // Row 7: Table Columns Header
        int currentRow = 7;
        string[] headers = new[]
        {
            "STT",
            "Đơn hàng",
            "Sản phẩm / Quy cách",
            "Chi tiết quy cách / Gói",
            "SL",
            "ĐVT",
            "Đơn giá",
            "Thành tiền",
            "Ghi chú"
        };

        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(currentRow, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.FontName = fontName;
            cell.Style.Font.FontSize = 11;
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = brandBlue;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Alignment.Horizontal = (i == 0 || i == 4 || i == 5) ? XLAlignmentHorizontalValues.Center :
                                              (i == 6 || i == 7) ? XLAlignmentHorizontalValues.Right :
                                              XLAlignmentHorizontalValues.Left;
        }
        ws.Row(currentRow).Height = 28;

        int firstDataRow = currentRow + 1;
        int stt = 1;
        var includedOrders = bill.Orders.Where(o => o.IsIncluded).ToList();

        foreach (var order in includedOrders)
        {
            var includedLines = order.Lines.Where(l => l.IsIncluded).ToList();
            if (includedLines.Count == 0) continue;

            string orderCodePrefix = !string.IsNullOrWhiteSpace(order.OrderCodeSnapshot) ? $"[{order.OrderCodeSnapshot}] " : "";
            string orderTitle = $"{orderCodePrefix}{order.OrderNameSnapshot} (Ngày {FormatDate(order.OrderDateSnapshot)})";

            foreach (var line in includedLines)
            {
                currentRow++;
                ws.Row(currentRow).Height = 24;

                // STT
                var cellStt = ws.Cell(currentRow, 1);
                cellStt.Value = stt++;
                cellStt.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // Order Name
                var cellOrder = ws.Cell(currentRow, 2);
                cellOrder.Value = orderTitle;

                if (line.BillingMethodSnapshot == BillingMethod.AlbumBasePlusExtra)
                {
                    int sheetCount = line.SheetCount ?? line.ScannedQuantity;
                    int inc = line.IncludedSheetsSnapshot ?? 10;
                    long baseP = line.BasePriceSnapshot ?? 0;
                    long extraP = line.ExtraSheetPriceSnapshot ?? 0;
                    int extraS = line.ExtraSheetCount ?? Math.Max(0, sheetCount - inc);

                    // Product
                    ws.Cell(currentRow, 3).Value = $"Album {line.VariantSnapshot ?? line.ProductNameSnapshot} — {sheetCount} tờ";
                    
                    // Detail
                    if (sheetCount >= inc)
                    {
                        if (extraS > 0)
                        {
                            ws.Cell(currentRow, 4).Value = $"Gói chuẩn {inc} tờ ({baseP:#,##0} đ) + {extraS} tờ phát sinh ({extraS * extraP:#,##0} đ)";
                        }
                        else
                        {
                            ws.Cell(currentRow, 4).Value = $"Gói chuẩn {inc} tờ ({baseP:#,##0} đ)";
                        }
                    }
                    else
                    {
                        ws.Cell(currentRow, 4).Value = $"{sheetCount} tờ (dưới gói chuẩn {inc} tờ)";
                    }

                    // Quantity & Unit
                    ws.Cell(currentRow, 5).Value = 1;
                    ws.Cell(currentRow, 5).Style.NumberFormat.Format = "#,##0";
                    ws.Cell(currentRow, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    ws.Cell(currentRow, 6).Value = "Cuốn";
                    ws.Cell(currentRow, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    // Unit price
                    ws.Cell(currentRow, 7).Value = line.LineTotal;
                    ws.Cell(currentRow, 7).Style.NumberFormat.Format = "#,##0\" đ\"";
                    ws.Cell(currentRow, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                    // Line total
                    ws.Cell(currentRow, 8).Value = line.LineTotal;
                    ws.Cell(currentRow, 8).Style.NumberFormat.Format = "#,##0\" đ\"";
                    ws.Cell(currentRow, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    ws.Cell(currentRow, 8).Style.Font.Bold = true;
                }
                else
                {
                    // Photo print line
                    ws.Cell(currentRow, 3).Value = line.ProductNameSnapshot;
                    ws.Cell(currentRow, 4).Value = line.VariantSnapshot ?? "";

                    // Quantity
                    ws.Cell(currentRow, 5).Value = line.BilledQuantity;
                    ws.Cell(currentRow, 5).Style.NumberFormat.Format = "#,##0";
                    ws.Cell(currentRow, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    // Unit
                    ws.Cell(currentRow, 6).Value = "Tấm";
                    ws.Cell(currentRow, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    // Unit price
                    ws.Cell(currentRow, 7).Value = line.BilledUnitPrice;
                    ws.Cell(currentRow, 7).Style.NumberFormat.Format = "#,##0\" đ\"";
                    ws.Cell(currentRow, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                    // Line total formula: =E{row}*G{row}
                    ws.Cell(currentRow, 8).FormulaA1 = $"=E{currentRow}*G{currentRow}";
                    ws.Cell(currentRow, 8).Style.NumberFormat.Format = "#,##0\" đ\"";
                    ws.Cell(currentRow, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    ws.Cell(currentRow, 8).Style.Font.Bold = true;
                }

                // Note
                ws.Cell(currentRow, 9).Value = line.Note ?? string.Empty;

                // Format fonts and borders
                for (int c = 1; c <= 9; c++)
                {
                    var cell = ws.Cell(currentRow, c);
                    cell.Style.Font.FontName = fontName;
                    cell.Style.Font.FontSize = 10;
                    cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    cell.Style.Border.OutsideBorderColor = borderLight;
                    cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                }
            }
        }

        int lastDataRow = Math.Max(firstDataRow, currentRow);

        // Divider
        currentRow++;
        ws.Row(currentRow).Height = 8;

        // Row Subtotal
        currentRow++;
        ws.Range(currentRow, 1, currentRow, 7).Merge();
        var subtotalLabel = ws.Cell(currentRow, 1);
        subtotalLabel.Value = "Tổng cộng tiền hàng:";
        subtotalLabel.Style.Font.FontName = fontName;
        subtotalLabel.Style.Font.FontSize = 11;
        subtotalLabel.Style.Font.Bold = true;
        subtotalLabel.Style.Font.FontColor = textSecondary;
        subtotalLabel.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        subtotalLabel.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        var subtotalCell = ws.Cell(currentRow, 8);
        if (lastDataRow >= firstDataRow && stt > 1)
        {
            subtotalCell.FormulaA1 = $"=SUM(H{firstDataRow}:H{lastDataRow})";
        }
        else
        {
            subtotalCell.Value = bill.ProductSubtotal;
        }
        subtotalCell.Style.Font.FontName = fontName;
        subtotalCell.Style.Font.FontSize = 11;
        subtotalCell.Style.Font.Bold = true;
        subtotalCell.Style.Font.FontColor = textPrimary;
        subtotalCell.Style.NumberFormat.Format = "#,##0\" đ\"";
        subtotalCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        subtotalCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Row(currentRow).Height = 24;

        int subtotalRow = currentRow;

        // Adjustments rows
        int adjustmentStartRow = currentRow + 1;
        foreach (var adj in bill.Adjustments)
        {
            if (adj.Amount == 0) continue;

            currentRow++;
            ws.Range(currentRow, 1, currentRow, 7).Merge();
            var adjLabel = ws.Cell(currentRow, 1);
            adjLabel.Value = $"{adj.Label}:";
            adjLabel.Style.Font.FontName = fontName;
            adjLabel.Style.Font.FontSize = 10;
            adjLabel.Style.Font.FontColor = textSecondary;
            adjLabel.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            adjLabel.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            var adjCell = ws.Cell(currentRow, 8);
            long adjVal = adj.Direction == AdjustmentDirection.Deduct ? -adj.Amount : adj.Amount;
            adjCell.Value = adjVal;
            adjCell.Style.Font.FontName = fontName;
            adjCell.Style.Font.FontSize = 10;
            adjCell.Style.Font.Bold = true;
            adjCell.Style.Font.FontColor = adj.Direction == AdjustmentDirection.Deduct ? XLColor.FromArgb(220, 38, 38) : textSecondary;
            adjCell.Style.NumberFormat.Format = "#,##0\" đ\"";
            adjCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            adjCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Row(currentRow).Height = 22;
        }
        int adjustmentEndRow = currentRow;

        // Grand Total Row
        currentRow++;
        ws.Range(currentRow, 1, currentRow, 7).Merge();
        var grandLabel = ws.Cell(currentRow, 1);
        grandLabel.Value = "TỔNG TIỀN THANH TOÁN:";
        grandLabel.Style.Font.FontName = fontName;
        grandLabel.Style.Font.FontSize = 13;
        grandLabel.Style.Font.Bold = true;
        grandLabel.Style.Font.FontColor = brandBlue;
        grandLabel.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        grandLabel.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        var grandCell = ws.Cell(currentRow, 8);
        if (adjustmentEndRow >= adjustmentStartRow)
        {
            grandCell.FormulaA1 = $"=H{subtotalRow}+SUM(H{adjustmentStartRow}:H{adjustmentEndRow})";
        }
        else
        {
            grandCell.FormulaA1 = $"=H{subtotalRow}";
        }
        grandCell.Style.Font.FontName = fontName;
        grandCell.Style.Font.FontSize = 14;
        grandCell.Style.Font.Bold = true;
        grandCell.Style.Font.FontColor = brandBlue;
        grandCell.Style.NumberFormat.Format = "#,##0\" đ\"";
        grandCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        grandCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        var grandRange = ws.Range(currentRow, 1, currentRow, 9);
        grandRange.Style.Fill.BackgroundColor = highlightBlue;
        grandRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
        grandRange.Style.Border.OutsideBorderColor = brandBlue;
        ws.Row(currentRow).Height = 32;

        // Note section
        if (!string.IsNullOrWhiteSpace(bill.Note))
        {
            currentRow += 2;
            ws.Range(currentRow, 1, currentRow, 9).Merge();
            var noteCell = ws.Cell(currentRow, 1);
            noteCell.Value = $"Ghi chú: {bill.Note}";
            noteCell.Style.Font.FontName = fontName;
            noteCell.Style.Font.FontSize = 11;
            noteCell.Style.Font.Italic = true;
            noteCell.Style.Font.FontColor = textSecondary;
            ws.Row(currentRow).Height = 22;
        }

        // Bank Payment Info Section
        if (!string.IsNullOrWhiteSpace(settings?.BankAccountNumber))
        {
            currentRow += 2;
            string bankName = VietnameseBanks.GetShortNameOrBin(settings.BankBinOrCode);
            string owner = !string.IsNullOrWhiteSpace(settings.BankAccountName) ? $" ({settings.BankAccountName.ToUpper()})" : "";

            ws.Range(currentRow, 1, currentRow, 9).Merge();
            var payCell = ws.Cell(currentRow, 1);
            payCell.Value = $"Thông tin chuyển khoản: {bankName} — Số TK: {settings.BankAccountNumber}{owner}  |  Nội dung CK: {bill.BillNumber}";
            payCell.Style.Font.FontName = fontName;
            payCell.Style.Font.FontSize = 10;
            payCell.Style.Font.Bold = true;
            payCell.Style.Font.FontColor = brandBlue;
            payCell.Style.Fill.BackgroundColor = highlightBlue;
            payCell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            payCell.Style.Border.OutsideBorderColor = borderLight;
            ws.Row(currentRow).Height = 24;
        }

        // Footer Thank You
        currentRow += 2;
        ws.Range(currentRow, 1, currentRow, 9).Merge();
        var footerCell = ws.Cell(currentRow, 1);
        string footerMsg = !string.IsNullOrWhiteSpace(settings?.InvoiceFooterMessage)
            ? settings.InvoiceFooterMessage
            : "Cảm ơn quý khách đã tin tưởng và ủng hộ dịch vụ!";
        footerCell.Value = footerMsg;
        footerCell.Style.Font.FontName = fontName;
        footerCell.Style.Font.FontSize = 10;
        footerCell.Style.Font.Italic = true;
        footerCell.Style.Font.FontColor = XLColor.FromArgb(148, 163, 184);
        footerCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Row(currentRow).Height = 20;

        // Adjust column widths
        ws.Columns().AdjustToContents();
        if (ws.Column(1).Width < 7) ws.Column(1).Width = 7;     // STT
        if (ws.Column(2).Width < 22) ws.Column(2).Width = 22;  // Đơn hàng
        if (ws.Column(3).Width < 28) ws.Column(3).Width = 28;  // Sản phẩm
        if (ws.Column(4).Width < 32) ws.Column(4).Width = 32;  // Chi tiết gói
        if (ws.Column(5).Width < 12) ws.Column(5).Width = 12;  // SL
        if (ws.Column(6).Width < 10) ws.Column(6).Width = 10;  // ĐVT
        if (ws.Column(7).Width < 16) ws.Column(7).Width = 16;  // Đơn giá
        if (ws.Column(8).Width < 18) ws.Column(8).Width = 18;  // Thành tiền
        if (ws.Column(9).Width < 20) ws.Column(9).Width = 20;  // Ghi chú

        workbook.SaveAs(destinationFilePath);
    }

    private static string FormatDate(string yyyyMmDd)
    {
        if (DateTime.TryParse(yyyyMmDd, out var dt))
        {
            return dt.ToString("dd/MM/yyyy");
        }
        return yyyyMmDd;
    }
}
