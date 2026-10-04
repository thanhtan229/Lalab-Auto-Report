using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LalabAutoReport.Infrastructure.Reporting;

/// <summary>
/// Thermal shipping label / package slip exporter (75x100mm format).
/// Exports crisp monochrome/grayscale images for thermal printing or sends directly to Windows PrintDialog.
/// </summary>
public class WpfShippingLabelExporter : IShippingLabelExporter
{
    public const double LabelWidth = 600;
    public const double LabelHeight = 800; // 3:4 ratio matches 75x100mm thermal labels

    private readonly ISettingsRepository _settingsRepository;
    private readonly ILogger<WpfShippingLabelExporter>? _logger;
    private static readonly CultureInfo ViCulture = new("vi-VN");

    public WpfShippingLabelExporter(ISettingsRepository settingsRepository, ILogger<WpfShippingLabelExporter>? logger = null)
    {
        _settingsRepository = settingsRepository;
        _logger = logger;
    }

    public async Task<string> ExportShippingLabelImageAsync(CustomerBill bill, string? destinationPath = null, CancellationToken cancellationToken = default)
    {
        var settings = await _settingsRepository.GetSettingsAsync(cancellationToken);
        string exportDir = destinationPath ?? string.Empty;

        if (string.IsNullOrWhiteSpace(exportDir))
        {
            exportDir = Path.Combine(settings.BillExportFolder, "ShippingLabels");
        }

        Directory.CreateDirectory(exportDir);

        string safeCustName = SanitizeCustomerName(bill.CustomerNameSnapshot);
        string fileName = $"Label_{bill.BillNumber}_{safeCustName}.png";
        string filePath = Path.Combine(exportDir, fileName);

        await RunOnStaThreadAsync(() =>
        {
            var visual = CreateLabelVisual(bill, settings);
            SaveVisualToPng(visual, filePath);
        });

        _logger?.LogInformation("Exported thermal shipping label for bill {BillNumber} to {FilePath}", bill.BillNumber, filePath);
        return filePath;
    }

    public async Task PrintShippingLabelAsync(CustomerBill bill, bool silent = false, CancellationToken cancellationToken = default)
    {
        var settings = await _settingsRepository.GetSettingsAsync(cancellationToken);

        await RunOnStaThreadAsync(() =>
        {
            var visual = CreateLabelVisual(bill, settings);
            SendVisualToPrinter(visual, $"Shipping Label - {bill.BillNumber}", settings, silent, specificPrinterName: null, _logger);
        });
    }

    public async Task<string> ExportOrderShippingLabelImageAsync(Order order, string? destinationPath = null, CancellationToken cancellationToken = default)
    {
        var settings = await _settingsRepository.GetSettingsAsync(cancellationToken);
        string exportDir = destinationPath ?? string.Empty;

        if (string.IsNullOrWhiteSpace(exportDir))
        {
            exportDir = Path.Combine(settings.BillExportFolder, "ShippingLabels");
        }

        Directory.CreateDirectory(exportDir);

        string custName = order.Customer?.CanonicalName ?? order.OriginalFolderName;
        string safeCustName = SanitizeCustomerName(custName);
        string code = !string.IsNullOrWhiteSpace(order.OrderCode) ? order.OrderCode : $"ORD-{order.Id}";
        string fileName = $"Label_Order_{code}_{safeCustName}.png";
        string filePath = Path.Combine(exportDir, fileName);

        await RunOnStaThreadAsync(() =>
        {
            var visual = CreateOrderLabelVisual(order, settings);
            SaveVisualToPng(visual, filePath);
        });

        _logger?.LogInformation("Exported thermal order shipping label for order {OrderCode} to {FilePath}", code, filePath);
        return filePath;
    }

    public async Task PrintOrderShippingLabelAsync(Order order, bool silent = false, CancellationToken cancellationToken = default)
    {
        var settings = await _settingsRepository.GetSettingsAsync(cancellationToken);
        string code = !string.IsNullOrWhiteSpace(order.OrderCode) ? order.OrderCode : $"ORD-{order.Id}";

        await RunOnStaThreadAsync(() =>
        {
            var visual = CreateOrderLabelVisual(order, settings);
            SendVisualToPrinter(visual, $"Order Shipping Label - {code}", settings, silent, specificPrinterName: null, _logger);
        });
    }

    public async Task PrintTestSampleLabelAsync(string? printerName = null, CancellationToken cancellationToken = default)
    {
        var settings = await _settingsRepository.GetSettingsAsync(cancellationToken);

        await RunOnStaThreadAsync(() =>
        {
            var visual = CreateTestSampleLabelVisual(settings, printerName);
            SendVisualToPrinter(visual, "Lalab Test Sample Label (75x100mm)", settings, silent: true, specificPrinterName: printerName, _logger);
        });
    }

    private static void SaveVisualToPng(Visual visual, string filePath)
    {
        var rtb = new RenderTargetBitmap((int)LabelWidth, (int)LabelHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));

        using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        encoder.Save(fileStream);
    }

    private static void SendVisualToPrinter(
        Visual visual, 
        string jobTitle, 
        AppSettings? settings, 
        bool silent, 
        string? specificPrinterName, 
        ILogger? logger)
    {
        try
        {
            var printDialog = new System.Windows.Controls.PrintDialog();
            string? printerToUse = !string.IsNullOrWhiteSpace(specificPrinterName)
                ? specificPrinterName
                : settings?.ThermalPrinterName;

            if (!string.IsNullOrWhiteSpace(printerToUse))
            {
                try
                {
                    var printServer = new System.Printing.LocalPrintServer();
                    var queue = printServer.GetPrintQueue(printerToUse);
                    if (queue != null)
                    {
                        printDialog.PrintQueue = queue;
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Không tìm thấy máy in {PrinterName}, chuyển sang máy in mặc định.", printerToUse);
                }
            }

            if (!silent)
            {
                if (printDialog.ShowDialog() != true)
                {
                    logger?.LogInformation("Người dùng đã hủy hộp thoại in cho {JobTitle}", jobTitle);
                    return;
                }
            }

            // Container for scaling to printable area
            var container = new ContainerVisual();
            double printWidth = LabelWidth;
            double printHeight = LabelHeight;

            try
            {
                if (printDialog.PrintQueue != null)
                {
                    var ticket = printDialog.PrintTicket ?? printDialog.PrintQueue.DefaultPrintTicket;
                    var capabilities = printDialog.PrintQueue.GetPrintCapabilities(ticket);
                    if (capabilities?.PageImageableArea != null && capabilities.PageImageableArea.ExtentWidth > 50 && capabilities.PageImageableArea.ExtentHeight > 50)
                    {
                        printWidth = capabilities.PageImageableArea.ExtentWidth;
                        printHeight = capabilities.PageImageableArea.ExtentHeight;
                    }
                    else if (printDialog.PrintableAreaWidth > 50 && printDialog.PrintableAreaHeight > 50)
                    {
                        printWidth = printDialog.PrintableAreaWidth;
                        printHeight = printDialog.PrintableAreaHeight;
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Không thể đọc kích thước trang in thực tế, dùng kích thước tem mặc định 75x100mm.");
            }

            double scaleX = printWidth / LabelWidth;
            double scaleY = printHeight / LabelHeight;
            double scale = Math.Min(scaleX, scaleY);

            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(new ScaleTransform(scale, scale));
            container.Transform = transformGroup;
            container.Children.Add(visual);

            printDialog.PrintVisual(container, jobTitle);
            logger?.LogInformation("Đã gửi lệnh in tem '{JobTitle}' tới máy in {PrinterName} (silent={Silent})", 
                jobTitle, printDialog.PrintQueue?.FullName ?? "Default", silent);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Lỗi khi gửi lệnh in '{JobTitle}' tới máy in", jobTitle);
            throw;
        }
    }

    private static async Task RunOnStaThreadAsync(Action action)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            action();
            return;
        }

        var tcs = new TaskCompletionSource();
        var staThread = new Thread(() =>
        {
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        staThread.SetApartmentState(ApartmentState.STA);
        staThread.IsBackground = true;
        staThread.Start();
        await tcs.Task;
    }

    public static DrawingVisual CreateLabelVisual(CustomerBill bill, AppSettings? settings)
    {
        const double width = LabelWidth;
        const double height = LabelHeight;
        const double marginX = 24;
        const double contentWidth = width - (marginX * 2);

        var typefaceBold = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var typefaceSemiBold = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var typefaceRegular = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var typefaceItalic = new Typeface(new FontFamily("Segoe UI"), FontStyles.Italic, FontWeights.Normal, FontStretches.Normal);

        var blackPen = new Pen(Brushes.Black, 1.5);
        var thinPen = new Pen(new SolidColorBrush(Color.FromRgb(100, 100, 100)), 1);
        var thickPen = new Pen(Brushes.Black, 2.5);

        var visual = new DrawingVisual();
        using var dc = visual.RenderOpen();

        // 1. Background (Pure White for Thermal Paper)
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));

        // Outer Border
        dc.DrawRectangle(null, blackPen, new Rect(12, 12, width - 24, height - 24));

        double currentY = 24;

        // 2. Sender / Workshop Info (Header)
        string workshopName = !string.IsNullOrWhiteSpace(settings?.WorkshopName) 
            ? settings.WorkshopName 
            : "XƯỞNG IN ẢNH CHUYÊN NGHIỆP";
        string workshopPhone = settings?.WorkshopPhone?.Trim() ?? string.Empty;
        string workshopAddr = settings?.WorkshopAddress?.Trim() ?? string.Empty;

        var logoBitmap = WpfJpegBillExporter.TryLoadBitmap(settings?.WorkshopLogoPath);
        if (logoBitmap != null)
        {
            // Draw small logo at top-right
            double maxLogoH = 40;
            double maxLogoW = 90;
            double scale = Math.Min(maxLogoH / logoBitmap.PixelHeight, maxLogoW / logoBitmap.PixelWidth);
            double logoW = logoBitmap.PixelWidth * scale;
            double logoH = logoBitmap.PixelHeight * scale;
            dc.DrawImage(logoBitmap, new Rect(width - marginX - logoW, currentY, logoW, logoH));
        }

        var wsNameFt = CreateText(workshopName.ToUpperInvariant(), typefaceBold, 14, Colors.Black);
        dc.DrawText(wsNameFt, new Point(marginX, currentY));
        currentY += wsNameFt.Height + 2;

        string wsContact = !string.IsNullOrWhiteSpace(workshopPhone) ? $"Hotline: {workshopPhone}" : "";
        if (!string.IsNullOrWhiteSpace(workshopAddr))
        {
            wsContact = !string.IsNullOrWhiteSpace(wsContact) ? $"{wsContact}  |  Đ/c: {workshopAddr}" : $"Đ/c: {workshopAddr}";
        }

        if (!string.IsNullOrWhiteSpace(wsContact))
        {
            var wsContactFt = CreateText(wsContact, typefaceRegular, 11, Color.FromRgb(60, 60, 60), contentWidth - 100);
            dc.DrawText(wsContactFt, new Point(marginX, currentY));
            currentY += wsContactFt.Height + 8;
        }
        else
        {
            currentY += 8;
        }

        // Divider
        dc.DrawLine(blackPen, new Point(marginX, currentY), new Point(width - marginX, currentY));
        currentY += 10;

        // 3. Document Title & Bill Number
        var titleFt = CreateText("PHIẾU GIAO HÀNG / PACKAGE SLIP", typefaceBold, 17, Colors.Black);
        dc.DrawText(titleFt, new Point(marginX, currentY));

        string dateStr = bill.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
        var dateFt = CreateText(dateStr, typefaceRegular, 12, Color.FromRgb(80, 80, 80));
        dc.DrawText(dateFt, new Point(width - marginX - dateFt.Width, currentY + 4));
        currentY += titleFt.Height + 4;

        var billNoFt = CreateText($"Mã hóa đơn: {bill.BillNumber}", typefaceBold, 14, Colors.Black);
        dc.DrawText(billNoFt, new Point(marginX, currentY));

        int includedOrdersCount = bill.Orders.Count(o => o.IsIncluded);
        string orderCountText = $"({includedOrdersCount} thư mục / đơn)";
        var orderCountFt = CreateText(orderCountText, typefaceRegular, 12, Color.FromRgb(80, 80, 80));
        dc.DrawText(orderCountFt, new Point(marginX + billNoFt.Width + 8, currentY + 2));
        currentY += billNoFt.Height + 8;

        // Divider
        dc.DrawLine(thickPen, new Point(marginX, currentY), new Point(width - marginX, currentY));
        currentY += 10;

        // 4. Recipient Section (NGƯỜI NHẬN - Highlighted)
        var recipientHeaderFt = CreateText("NGƯỜI NHẬN / RECIPIENT:", typefaceBold, 12, Color.FromRgb(60, 60, 60));
        dc.DrawText(recipientHeaderFt, new Point(marginX, currentY));
        currentY += recipientHeaderFt.Height + 4;

        // Customer Name
        string customerName = string.IsNullOrWhiteSpace(bill.CustomerNameSnapshot) ? "Khách vãng lai" : bill.CustomerNameSnapshot;
        var custNameFt = CreateText(customerName, typefaceBold, 20, Colors.Black, contentWidth);
        dc.DrawText(custNameFt, new Point(marginX, currentY));
        currentY += custNameFt.Height + 4;

        // Phone
        string phone = !string.IsNullOrWhiteSpace(bill.PhoneSnapshot) ? bill.PhoneSnapshot : "Chưa có SĐT";
        var phoneFt = CreateText($"SĐT: {phone}", typefaceBold, 16, Colors.Black);
        dc.DrawText(phoneFt, new Point(marginX, currentY));
        currentY += phoneFt.Height + 4;

        // Shipping Address
        string address = !string.IsNullOrWhiteSpace(bill.ShippingAddressSnapshot) 
            ? bill.ShippingAddressSnapshot 
            : "Địa chỉ: Nhận tại xưởng / Giao theo hẹn";
        var addrFt = CreateText(address, typefaceRegular, 13, Colors.Black, contentWidth);
        dc.DrawText(addrFt, new Point(marginX, currentY));
        currentY += addrFt.Height + 10;

        // Divider
        dc.DrawLine(blackPen, new Point(marginX, currentY), new Point(width - marginX, currentY));
        currentY += 10;

        // 5. Items Summary Section
        var itemsHeaderFt = CreateText("DANH SÁCH SẢN PHẨM IN:", typefaceBold, 12, Color.FromRgb(60, 60, 60));
        dc.DrawText(itemsHeaderFt, new Point(marginX, currentY));
        currentY += itemsHeaderFt.Height + 6;

        var allLines = (bill.Lines.Count > 0 ? bill.Lines : bill.Orders.SelectMany(o => o.Lines))
            .Where(l => l.IsIncluded)
            .ToList();

        // Group lines by product and variant
        var itemGroups = allLines
            .GroupBy(l => $"{l.ProductNameSnapshot}|{l.VariantSnapshot ?? ""}|{l.SheetCount ?? 0}")
            .Select(g =>
            {
                var first = g.First();
                int totalQty = g.Sum(x => x.BilledQuantity);
                return new
                {
                    Name = first.ProductNameSnapshot,
                    Variant = first.VariantSnapshot,
                    SheetCount = first.SheetCount,
                    Quantity = totalQty
                };
            })
            .ToList();

        int maxItemsToShow = 5;
        var displayItems = itemGroups.Take(maxItemsToShow).ToList();

        foreach (var item in displayItems)
        {
            string itemDesc = item.Name;
            if (!string.IsNullOrWhiteSpace(item.Variant))
            {
                itemDesc += $" ({item.Variant})";
            }
            if (item.SheetCount > 0)
            {
                itemDesc += $" - {item.SheetCount} trang";
            }

            var itemDescFt = CreateText($"• {itemDesc}", typefaceRegular, 12, Colors.Black, contentWidth - 80);
            dc.DrawText(itemDescFt, new Point(marginX + 6, currentY));

            var itemQtyFt = CreateText($"SL: {item.Quantity}", typefaceBold, 12, Colors.Black);
            dc.DrawText(itemQtyFt, new Point(width - marginX - itemQtyFt.Width - 6, currentY));

            currentY += Math.Max(itemDescFt.Height, itemQtyFt.Height) + 4;
        }

        if (itemGroups.Count > maxItemsToShow)
        {
            int remaining = itemGroups.Count - maxItemsToShow;
            var moreFt = CreateText($"... và {remaining} loại sản phẩm khác (xem chi tiết trên hóa đơn)", typefaceItalic, 11, Color.FromRgb(100, 100, 100));
            dc.DrawText(moreFt, new Point(marginX + 6, currentY));
            currentY += moreFt.Height + 4;
        }

        int totalPrintCount = allLines.Sum(l => l.BilledQuantity);
        var totalQtyFt = CreateText($"Tổng số lượng in: {totalPrintCount} sản phẩm", typefaceSemiBold, 12, Colors.Black);
        dc.DrawText(totalQtyFt, new Point(marginX + 6, currentY));
        currentY += totalQtyFt.Height + 10;

        // Divider
        dc.DrawLine(thickPen, new Point(marginX, currentY), new Point(width - marginX, currentY));
        currentY += 12;

        // 6. Payment / COD Section (PROMINENT - Avoid courier collection mistakes!)
        double codBoxHeight = 82;
        var codRect = new Rect(marginX, currentY, contentWidth, codBoxHeight);

        if (bill.IsPaid)
        {
            // Paid Box: Solid gray background, crisp black border
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(240, 240, 240)), thickPen, codRect, 6, 6);

            var paidTitleFt = CreateText("✓ ĐÃ THANH TOÁN (PAID)", typefaceBold, 18, Colors.Black);
            dc.DrawText(paidTitleFt, new Point(marginX + (contentWidth - paidTitleFt.Width) / 2, currentY + 12));

            var paidSubFt = CreateText("KHÔNG THU TIỀN NGƯỜI NHẬN (COD = 0 đ)", typefaceBold, 14, Colors.Black);
            dc.DrawText(paidSubFt, new Point(marginX + (contentWidth - paidSubFt.Width) / 2, currentY + 44));
        }
        else
        {
            // Unpaid Box: Heavy black border with clear COD amount
            dc.DrawRoundedRectangle(Brushes.White, new Pen(Brushes.Black, 3), codRect, 6, 6);

            var codTitleFt = CreateText("TIỀN THU HỘ (COD):", typefaceBold, 14, Colors.Black);
            dc.DrawText(codTitleFt, new Point(marginX + 16, currentY + 14));

            string amountStr = bill.GrandTotal.ToString("#,##0", ViCulture) + " đ";
            var amountFt = CreateText(amountStr, typefaceBold, 26, Colors.Black);
            dc.DrawText(amountFt, new Point(width - marginX - 16 - amountFt.Width, currentY + 10));

            var codNoteFt = CreateText("(Shipper vui lòng thu đủ số tiền trước khi bàn giao hàng)", typefaceItalic, 11, Color.FromRgb(60, 60, 60));
            dc.DrawText(codNoteFt, new Point(marginX + 16, currentY + 52));
        }

        currentY += codBoxHeight + 14;

        // 7. Footer & QR Code Section
        double qrSize = 110;
        var qrRect = new Rect(marginX, currentY, qrSize, qrSize);

        // Decide QR Code Payload:
        // If unpaid and VietQR enabled: render VietQR payload for quick scan-to-pay
        // Otherwise: render bill number payload for package tracking / scanning
        string qrPayload;
        if (!bill.IsPaid && settings?.EnableVietQrOnBill == true && !string.IsNullOrWhiteSpace(settings.BankAccountNumber))
        {
            qrPayload = VietQrGenerator.BuildVietQrPayload(
                settings.BankBinOrCode, 
                settings.BankAccountNumber, 
                bill.GrandTotal, 
                bill.BillNumber);
        }
        else
        {
            qrPayload = $"BILL:{bill.BillNumber}|{bill.CustomerNameSnapshot}|{bill.GrandTotal}";
        }

        if (!string.IsNullOrWhiteSpace(qrPayload))
        {
            VietQrGenerator.RenderQrCode(dc, qrRect, qrPayload, Brushes.Black, Brushes.White);
            dc.DrawRectangle(null, thinPen, qrRect);
        }

        // Instructions and Notes beside QR Code
        double rightTextX = marginX + qrSize + 16;
        double rightTextWidth = contentWidth - qrSize - 16;
        double rightY = currentY + 4;

        if (!string.IsNullOrWhiteSpace(bill.Note))
        {
            var noteFt = CreateText($"Ghi chú: {bill.Note}", typefaceBold, 12, Colors.Black, rightTextWidth);
            dc.DrawText(noteFt, new Point(rightTextX, rightY));
            rightY += noteFt.Height + 8;
        }

        var checkGoodsFt = CreateText("• Quý khách vui lòng đồng kiểm hàng khi nhận.", typefaceRegular, 11, Colors.Black, rightTextWidth);
        dc.DrawText(checkGoodsFt, new Point(rightTextX, rightY));
        rightY += checkGoodsFt.Height + 4;

        var hotlineFt = CreateText($"• Hỗ trợ & phản hồi: {workshopPhone}", typefaceSemiBold, 11, Colors.Black, rightTextWidth);
        dc.DrawText(hotlineFt, new Point(rightTextX, rightY));
        rightY += hotlineFt.Height + 4;

        var scanFt = CreateText(
            !bill.IsPaid && settings?.EnableVietQrOnBill == true ? "• Quét QR bên cạnh để chuyển khoản COD." : "• Quét mã QR để tra cứu thông tin đơn.",
            typefaceItalic, 10, Color.FromRgb(80, 80, 80), rightTextWidth);
        dc.DrawText(scanFt, new Point(rightTextX, rightY));

        return visual;
    }

    private static FormattedText CreateText(string text, Typeface typeface, double fontSize, Color color, double maxTextWidth = 0)
    {
        var ft = new FormattedText(
            text,
            ViCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            new SolidColorBrush(color),
            VisualTreeHelper.GetDpi(new DrawingVisual()).PixelsPerDip
        );

        if (maxTextWidth > 0)
        {
            ft.MaxTextWidth = maxTextWidth;
        }

        return ft;
    }

    public static DrawingVisual CreateOrderLabelVisual(Order order, AppSettings? settings)
    {
        const double width = LabelWidth;
        const double height = LabelHeight;
        const double marginX = 24;
        const double contentWidth = width - (marginX * 2);

        var typefaceBold = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var typefaceSemiBold = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var typefaceRegular = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var typefaceItalic = new Typeface(new FontFamily("Segoe UI"), FontStyles.Italic, FontWeights.Normal, FontStretches.Normal);

        var blackPen = new Pen(Brushes.Black, 1.5);
        var thinPen = new Pen(new SolidColorBrush(Color.FromRgb(100, 100, 100)), 1);
        var thickPen = new Pen(Brushes.Black, 2.5);

        var visual = new DrawingVisual();
        using var dc = visual.RenderOpen();

        // 1. Background (Pure White for Thermal Paper)
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));

        // Outer Border
        dc.DrawRectangle(null, blackPen, new Rect(12, 12, width - 24, height - 24));

        double currentY = 24;

        // 2. Sender / Workshop Info (Header)
        string workshopName = !string.IsNullOrWhiteSpace(settings?.WorkshopName) 
            ? settings.WorkshopName 
            : "XƯỞNG IN ẢNH CHUYÊN NGHIỆP";
        string workshopPhone = settings?.WorkshopPhone?.Trim() ?? string.Empty;
        string workshopAddr = settings?.WorkshopAddress?.Trim() ?? string.Empty;

        var logoBitmap = WpfJpegBillExporter.TryLoadBitmap(settings?.WorkshopLogoPath);
        if (logoBitmap != null)
        {
            double maxLogoH = 40;
            double maxLogoW = 90;
            double scale = Math.Min(maxLogoH / logoBitmap.PixelHeight, maxLogoW / logoBitmap.PixelWidth);
            double logoW = logoBitmap.PixelWidth * scale;
            double logoH = logoBitmap.PixelHeight * scale;
            dc.DrawImage(logoBitmap, new Rect(width - marginX - logoW, currentY, logoW, logoH));
        }

        var wsNameFt = CreateText(workshopName.ToUpperInvariant(), typefaceBold, 14, Colors.Black);
        dc.DrawText(wsNameFt, new Point(marginX, currentY));
        currentY += wsNameFt.Height + 2;

        string wsContact = !string.IsNullOrWhiteSpace(workshopPhone) ? $"Hotline: {workshopPhone}" : "";
        if (!string.IsNullOrWhiteSpace(workshopAddr))
        {
            wsContact = !string.IsNullOrWhiteSpace(wsContact) ? $"{wsContact}  |  Đ/c: {workshopAddr}" : $"Đ/c: {workshopAddr}";
        }

        if (!string.IsNullOrWhiteSpace(wsContact))
        {
            var wsContactFt = CreateText(wsContact, typefaceRegular, 11, Color.FromRgb(60, 60, 60), contentWidth - 100);
            dc.DrawText(wsContactFt, new Point(marginX, currentY));
            currentY += wsContactFt.Height + 8;
        }
        else
        {
            currentY += 8;
        }

        // Divider
        dc.DrawLine(blackPen, new Point(marginX, currentY), new Point(width - marginX, currentY));
        currentY += 10;

        // 3. Document Title & Order Code
        var titleFt = CreateText("PHIẾU GIAO HÀNG / TEM ĐÓNG GÓI", typefaceBold, 17, Colors.Black);
        dc.DrawText(titleFt, new Point(marginX, currentY));

        string dateStr = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
        var dateFt = CreateText(dateStr, typefaceRegular, 12, Color.FromRgb(80, 80, 80));
        dc.DrawText(dateFt, new Point(width - marginX - dateFt.Width, currentY + 4));
        currentY += titleFt.Height + 4;

        string orderCode = !string.IsNullOrWhiteSpace(order.OrderCode) ? order.OrderCode : $"ORD-{order.Id}";
        var orderNoFt = CreateText($"Mã đơn: {orderCode}", typefaceBold, 14, Colors.Black);
        dc.DrawText(orderNoFt, new Point(marginX, currentY));

        if (!string.IsNullOrWhiteSpace(order.WorkDate))
        {
            var workDateFt = CreateText($"(Ngày: {order.WorkDate})", typefaceRegular, 12, Color.FromRgb(80, 80, 80));
            dc.DrawText(workDateFt, new Point(marginX + orderNoFt.Width + 8, currentY + 2));
        }
        currentY += orderNoFt.Height + 8;

        // Divider
        dc.DrawLine(thickPen, new Point(marginX, currentY), new Point(width - marginX, currentY));
        currentY += 10;

        // 4. Recipient Section (NGƯỜI NHẬN)
        var recipientHeaderFt = CreateText("NGƯỜI NHẬN / RECIPIENT:", typefaceBold, 12, Color.FromRgb(60, 60, 60));
        dc.DrawText(recipientHeaderFt, new Point(marginX, currentY));
        currentY += recipientHeaderFt.Height + 4;

        string customerName = order.Customer?.CanonicalName ?? order.OriginalFolderName;
        if (string.IsNullOrWhiteSpace(customerName)) customerName = "Khách hàng";
        var custNameFt = CreateText(customerName, typefaceBold, 20, Colors.Black, contentWidth);
        dc.DrawText(custNameFt, new Point(marginX, currentY));
        currentY += custNameFt.Height + 4;

        string phone = !string.IsNullOrWhiteSpace(order.Customer?.Phone) ? order.Customer.Phone : "Chưa có SĐT";
        var phoneFt = CreateText($"SĐT: {phone}", typefaceBold, 16, Colors.Black);
        dc.DrawText(phoneFt, new Point(marginX, currentY));
        currentY += phoneFt.Height + 4;

        string address = !string.IsNullOrWhiteSpace(order.Customer?.Address)
            ? order.Customer.Address
            : "Địa chỉ: Nhận tại xưởng / Giao theo hẹn";
        var addrFt = CreateText(address, typefaceRegular, 13, Colors.Black, contentWidth);
        dc.DrawText(addrFt, new Point(marginX, currentY));
        currentY += addrFt.Height + 10;

        // Divider
        dc.DrawLine(blackPen, new Point(marginX, currentY), new Point(width - marginX, currentY));
        currentY += 10;

        // 5. Items Summary Section
        var itemsHeaderFt = CreateText("DANH SÁCH SẢN PHẨM IN:", typefaceBold, 12, Color.FromRgb(60, 60, 60));
        dc.DrawText(itemsHeaderFt, new Point(marginX, currentY));
        currentY += itemsHeaderFt.Height + 6;

        var items = order.Items ?? new List<OrderItemScan>();
        var itemGroups = items
            .GroupBy(i => $"{i.PrintSpecification?.CanonicalName ?? i.SpecificationFolderName}|{i.PrintSpecification?.CanonicalSize ?? ""}")
            .Select(g =>
            {
                var first = g.First();
                int totalQty = g.Sum(x => x.BillQuantity ?? x.PrintCount ?? 0);
                string specName = first.PrintSpecification?.CanonicalName ?? first.SpecificationFolderName;
                string? size = first.PrintSpecification?.CanonicalSize;
                return new { Name = specName, Size = size, Quantity = totalQty };
            })
            .ToList();

        int maxItemsToShow = 4;
        var displayItems = itemGroups.Take(maxItemsToShow).ToList();

        foreach (var it in displayItems)
        {
            string itemDesc = it.Name;
            if (!string.IsNullOrWhiteSpace(it.Size))
            {
                itemDesc += $" ({it.Size})";
            }

            var itemDescFt = CreateText($"• {itemDesc}", typefaceRegular, 12, Colors.Black, contentWidth - 80);
            dc.DrawText(itemDescFt, new Point(marginX + 6, currentY));

            var itemQtyFt = CreateText($"SL: {it.Quantity}", typefaceBold, 12, Colors.Black);
            dc.DrawText(itemQtyFt, new Point(width - marginX - itemQtyFt.Width - 6, currentY));

            currentY += Math.Max(itemDescFt.Height, itemQtyFt.Height) + 4;
        }

        if (itemGroups.Count > maxItemsToShow)
        {
            int remaining = itemGroups.Count - maxItemsToShow;
            var moreFt = CreateText($"... và {remaining} loại quy cách khác", typefaceItalic, 11, Color.FromRgb(100, 100, 100));
            dc.DrawText(moreFt, new Point(marginX + 6, currentY));
            currentY += moreFt.Height + 4;
        }

        int totalPrintCount = items.Sum(i => i.BillQuantity ?? i.PrintCount ?? 0);
        var totalQtyFt = CreateText($"Tổng số lượng in: {totalPrintCount} sản phẩm", typefaceSemiBold, 12, Colors.Black);
        dc.DrawText(totalQtyFt, new Point(marginX + 6, currentY));
        currentY += totalQtyFt.Height + 10;

        // Divider
        dc.DrawLine(thickPen, new Point(marginX, currentY), new Point(width - marginX, currentY));
        currentY += 12;

        // 6. Note / Order Notice Box
        double noticeBoxHeight = 82;
        var noticeRect = new Rect(marginX, currentY, contentWidth, noticeBoxHeight);

        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(245, 245, 245)), thinPen, noticeRect, 6, 6);

        if (!string.IsNullOrWhiteSpace(order.Note))
        {
            var noteTitleFt = CreateText("GHI CHÚ ĐÓNG GÓI:", typefaceBold, 12, Colors.Black);
            dc.DrawText(noteTitleFt, new Point(marginX + 12, currentY + 10));

            var noteContentFt = CreateText(order.Note, typefaceBold, 14, Color.FromRgb(20, 20, 20), contentWidth - 24);
            dc.DrawText(noteContentFt, new Point(marginX + 12, currentY + 30));
        }
        else
        {
            var unbilledFt = CreateText("ĐƠN HÀNG XƯỞNG IN (CHƯA XUẤT HÓA ĐƠN)", typefaceBold, 14, Colors.Black);
            dc.DrawText(unbilledFt, new Point(marginX + (contentWidth - unbilledFt.Width) / 2, currentY + 16));

            var checkFt = CreateText("Vui lòng kiểm đếm kỹ số lượng và đối chiếu khi giao", typefaceRegular, 12, Color.FromRgb(60, 60, 60));
            dc.DrawText(checkFt, new Point(marginX + (contentWidth - checkFt.Width) / 2, currentY + 44));
        }

        currentY += noticeBoxHeight + 14;

        // 7. Footer & QR Code Section
        double qrSize = 100;
        var qrRect = new Rect(marginX, currentY, qrSize, qrSize);
        string qrPayload = $"ORD:{orderCode}|{customerName}|{totalPrintCount}";

        VietQrGenerator.RenderQrCode(dc, qrRect, qrPayload, Brushes.Black, Brushes.White);
        dc.DrawRectangle(null, thinPen, qrRect);

        double rightTextX = marginX + qrSize + 16;
        double rightTextWidth = contentWidth - qrSize - 16;
        double rightY = currentY + 6;

        var checkGoodsFt = CreateText("• Quý khách vui lòng đồng kiểm hàng khi nhận.", typefaceRegular, 11, Colors.Black, rightTextWidth);
        dc.DrawText(checkGoodsFt, new Point(rightTextX, rightY));
        rightY += checkGoodsFt.Height + 4;

        var hotlineFt = CreateText($"• Hotline xưởng in: {workshopPhone}", typefaceSemiBold, 11, Colors.Black, rightTextWidth);
        dc.DrawText(hotlineFt, new Point(rightTextX, rightY));
        rightY += hotlineFt.Height + 4;

        var scanFt = CreateText("• Quét mã QR bên cạnh để tra cứu thông tin đơn hàng.", typefaceItalic, 10, Color.FromRgb(80, 80, 80), rightTextWidth);
        dc.DrawText(scanFt, new Point(rightTextX, rightY));

        return visual;
    }

    public static DrawingVisual CreateTestSampleLabelVisual(AppSettings? settings, string? printerName)
    {
        const double width = LabelWidth;
        const double height = LabelHeight;
        const double marginX = 24;
        const double contentWidth = width - (marginX * 2);

        var typefaceBold = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var typefaceSemiBold = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var typefaceRegular = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var typefaceItalic = new Typeface(new FontFamily("Segoe UI"), FontStyles.Italic, FontWeights.Normal, FontStretches.Normal);

        var blackPen = new Pen(Brushes.Black, 2);
        var thinPen = new Pen(Brushes.Black, 1);

        var visual = new DrawingVisual();
        using var dc = visual.RenderOpen();

        // Background
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
        dc.DrawRectangle(null, blackPen, new Rect(12, 12, width - 24, height - 24));

        double currentY = 30;

        string wsName = !string.IsNullOrWhiteSpace(settings?.WorkshopName) ? settings.WorkshopName : "LALAB PHOTO PRINT";
        var headerFt = CreateText(wsName.ToUpperInvariant(), typefaceBold, 16, Colors.Black);
        dc.DrawText(headerFt, new Point(marginX + (contentWidth - headerFt.Width) / 2, currentY));
        currentY += headerFt.Height + 10;

        dc.DrawLine(blackPen, new Point(marginX, currentY), new Point(width - marginX, currentY));
        currentY += 14;

        var titleFt = CreateText("TEM KIỂM TRA MÁY IN NHIỆT", typefaceBold, 18, Colors.Black);
        dc.DrawText(titleFt, new Point(marginX + (contentWidth - titleFt.Width) / 2, currentY));
        currentY += titleFt.Height + 12;

        var sizeFt = CreateText("KHỔ GIẤY CHUẨN: 75 x 100 mm", typefaceBold, 14, Colors.Black);
        dc.DrawText(sizeFt, new Point(marginX + (contentWidth - sizeFt.Width) / 2, currentY));
        currentY += sizeFt.Height + 16;

        // Info box
        var infoRect = new Rect(marginX, currentY, contentWidth, 140);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(240, 240, 240)), thinPen, infoRect, 6, 6);

        double infoY = currentY + 12;
        string printerDisplay = !string.IsNullOrWhiteSpace(printerName) ? printerName : (settings?.ThermalPrinterName ?? "Máy in mặc định của hệ thống");
        var prFt = CreateText($"• Máy in: {printerDisplay}", typefaceSemiBold, 13, Colors.Black, contentWidth - 24);
        dc.DrawText(prFt, new Point(marginX + 12, infoY));
        infoY += prFt.Height + 8;

        string timeStr = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
        var timeFt = CreateText($"• Thời gian in: {timeStr}", typefaceRegular, 13, Colors.Black);
        dc.DrawText(timeFt, new Point(marginX + 12, infoY));
        infoY += timeFt.Height + 8;

        var statusFt = CreateText("• Trạng thái kết nối: SẴN SÀNG HOẠT ĐỘNG ✓", typefaceBold, 13, Colors.Black);
        dc.DrawText(statusFt, new Point(marginX + 12, infoY));

        currentY += 156;

        // Contrast and Line Pattern Test
        dc.DrawRectangle(Brushes.Black, null, new Rect(marginX, currentY, contentWidth, 36));
        var contrastFt = CreateText("KIỂM TRA ĐỘ ĐẬM VÀ ĐỘ NÉT CỦA MÁY IN", typefaceBold, 12, Colors.White);
        dc.DrawText(contrastFt, new Point(marginX + (contentWidth - contrastFt.Width) / 2, currentY + 8));
        currentY += 50;

        // QR Code test
        double qrSize = 120;
        var qrRect = new Rect(marginX + (contentWidth - qrSize) / 2, currentY, qrSize, qrSize);
        VietQrGenerator.RenderQrCode(dc, qrRect, "LALAB-THERMAL-PRINTER-OK-75x100mm", Brushes.Black, Brushes.White);
        dc.DrawRectangle(null, thinPen, qrRect);
        currentY += qrSize + 16;

        var footerFt = CreateText("Nếu bạn thấy tem này in rõ nét, máy in nhiệt đã sẵn sàng!", typefaceItalic, 12, Color.FromRgb(60, 60, 60));
        dc.DrawText(footerFt, new Point(marginX + (contentWidth - footerFt.Width) / 2, currentY));

        return visual;
    }

    private static string SanitizeCustomerName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Guest";
        var sb = new StringBuilder();
        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(c);
            else if (c == ' ' || c == '-' || c == '_')
                sb.Append('_');
        }
        string sanitized = sb.ToString().Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? "Customer" : sanitized;
    }
}

