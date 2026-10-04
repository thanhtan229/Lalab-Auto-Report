using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClosedXML.Excel;
using FluentAssertions;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Infrastructure.Data;
using LalabAutoReport.Infrastructure.Reporting;
using Xunit;

namespace LalabAutoReport.Tests;

public class WhiteLabelAndQrSettingsTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly SqliteSettingsRepository _settingsRepo;
    private readonly ClosedXmlBillExporter _excelExporter;
    private readonly WpfJpegBillExporter _jpegExporter;
    private readonly WpfShippingLabelExporter _shippingExporter;

    public WhiteLabelAndQrSettingsTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "WhiteLabelTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        string dbPath = Path.Combine(_tempRoot, "test_settings.db");
        var connectionFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connectionFactory);
        migrator.MigrateAsync().GetAwaiter().GetResult();

        _settingsRepo = new SqliteSettingsRepository(connectionFactory);
        _excelExporter = new ClosedXmlBillExporter(_settingsRepo);
        _jpegExporter = new WpfJpegBillExporter(_settingsRepo);
        _shippingExporter = new WpfShippingLabelExporter(_settingsRepo);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, true);
            }
        }
        catch { }
    }

    private string CreateDummyPngImage(int width, int height, string fileName)
    {
        string filePath = Path.Combine(_tempRoot, fileName);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.DarkBlue, null, new System.Windows.Rect(0, 0, width, height));
        }
        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        encoder.Save(fs);
        return filePath;
    }

    private CustomerBill CreateSampleBill()
    {
        return new CustomerBill
        {
            Id = 101,
            BillNumber = "BILL-20261001-001",
            CustomerNameSnapshot = "Studio Nắng Mới",
            PhoneSnapshot = "0912345678",
            PeriodStart = "2026-10-01",
            PeriodEnd = "2026-10-01",
            ProductSubtotal = 450000,
            GrandTotal = 450000,
            Note = "Giao hàng buổi sáng",
            CreatedAt = DateTimeOffset.UtcNow,
            Orders = new List<CustomerBillOrder>
            {
                new CustomerBillOrder
                {
                    OrderNameSnapshot = "Đơn in ảnh phóng",
                    OrderDateSnapshot = "2026-10-01",
                    IsIncluded = true,
                    Subtotal = 450000,
                    Lines = new List<CustomerBillLine>
                    {
                        new CustomerBillLine
                        {
                            ProductNameSnapshot = "Ảnh ép gỗ 30x45",
                            BilledQuantity = 3,
                            BilledUnitPrice = 150000,
                            LineTotal = 450000,
                            IsIncluded = true,
                            BillingMethodSnapshot = BillingMethod.FileCount
                        }
                    }
                }
            }
        };
    }

    [Fact]
    public async Task SettingsRepository_SavesAndLoads_AllWhiteLabelAndQrFields()
    {
        string logoPath = Path.Combine(_tempRoot, "my_logo.png");
        string qrPath = Path.Combine(_tempRoot, "my_qr.png");

        var settingsToSave = new AppSettings
        {
            RootFolder = _tempRoot,
            BillExportFolder = Path.Combine(_tempRoot, "Bills"),
            WorkshopName = "XƯỞNG IN ÁNH SÁNG",
            WorkshopSlogan = "Chất lượng là danh dự",
            WorkshopPhone = "0987.654.321",
            WorkshopAddress = "Số 123 Đường In Ấn, TP. Hà Nội",
            InvoiceFooterMessage = "Cảm ơn quý khách đã đồng hành cùng Ánh Sáng!",
            WorkshopLogoPath = logoPath,
            QrMode = QrDisplayMode.CustomImage,
            CustomQrImagePath = qrPath,
            BankBinOrCode = "970415", // VietinBank
            BankAccountNumber = "109876543210",
            BankAccountName = "NGUYEN VAN ANH"
        };

        await _settingsRepo.SaveSettingsAsync(settingsToSave);

        var loaded = await _settingsRepo.GetSettingsAsync();

        loaded.WorkshopName.Should().Be("XƯỞNG IN ÁNH SÁNG");
        loaded.WorkshopSlogan.Should().Be("Chất lượng là danh dự");
        loaded.WorkshopPhone.Should().Be("0987.654.321");
        loaded.WorkshopAddress.Should().Be("Số 123 Đường In Ấn, TP. Hà Nội");
        loaded.InvoiceFooterMessage.Should().Be("Cảm ơn quý khách đã đồng hành cùng Ánh Sáng!");
        loaded.WorkshopLogoPath.Should().Be(logoPath);
        loaded.QrMode.Should().Be(QrDisplayMode.CustomImage);
        loaded.CustomQrImagePath.Should().Be(qrPath);
        loaded.BankBinOrCode.Should().Be("970415");
        loaded.BankAccountNumber.Should().Be("109876543210");
        loaded.BankAccountName.Should().Be("NGUYEN VAN ANH");
    }

    [Fact]
    public async Task WpfJpegBillExporter_RendersWithCustomImageQr_Successfully()
    {
        string dummyQr = CreateDummyPngImage(120, 120, "custom_qr.png");

        await _settingsRepo.SaveSettingsAsync(new AppSettings
        {
            RootFolder = _tempRoot,
            BillExportFolder = Path.Combine(_tempRoot, "Bills"),
            WorkshopName = "XƯỞNG IN HOÀNG GIA",
            WorkshopSlogan = "Đẳng cấp in ấn",
            WorkshopPhone = "0911.222.333",
            QrMode = QrDisplayMode.CustomImage,
            CustomQrImagePath = dummyQr,
            BankAccountNumber = "999888777",
            BankAccountName = "HOANG GIA LAB"
        });

        var bill = CreateSampleBill();
        string jpegPath = await _jpegExporter.ExportBillToJpegAsync(bill);

        File.Exists(jpegPath).Should().BeTrue();
        new FileInfo(jpegPath).Length.Should().BeGreaterThan(5000);
    }

    [Fact]
    public async Task WpfJpegBillExporter_RendersWithQrDisabled_Successfully()
    {
        await _settingsRepo.SaveSettingsAsync(new AppSettings
        {
            RootFolder = _tempRoot,
            BillExportFolder = Path.Combine(_tempRoot, "Bills"),
            WorkshopName = "XƯỞNG IN TIỀN MẶT",
            QrMode = QrDisplayMode.None,
            InvoiceFooterMessage = "Quý khách vui lòng thanh toán tiền mặt tại quầy!"
        });

        var bill = CreateSampleBill();
        string jpegPath = await _jpegExporter.ExportBillToJpegAsync(bill);

        File.Exists(jpegPath).Should().BeTrue();
        new FileInfo(jpegPath).Length.Should().BeGreaterThan(5000);
    }

    [Fact]
    public async Task WpfJpegBillExporter_RendersWithLogo_Successfully()
    {
        string dummyLogo = CreateDummyPngImage(100, 50, "workshop_logo.png");

        await _settingsRepo.SaveSettingsAsync(new AppSettings
        {
            RootFolder = _tempRoot,
            BillExportFolder = Path.Combine(_tempRoot, "Bills"),
            WorkshopName = "XƯỞNG IN LOGO VIP",
            WorkshopLogoPath = dummyLogo,
            QrMode = QrDisplayMode.VietQrAuto,
            BankBinOrCode = "970422",
            BankAccountNumber = "1234567890",
            BankAccountName = "CHỦ LAB VIP"
        });

        var bill = CreateSampleBill();
        string jpegPath = await _jpegExporter.ExportBillToJpegAsync(bill);

        File.Exists(jpegPath).Should().BeTrue();
        new FileInfo(jpegPath).Length.Should().BeGreaterThan(5000);
    }

    [Fact]
    public async Task ClosedXmlBillExporter_RendersDynamicWorkshop_AndBankInfo()
    {
        await _settingsRepo.SaveSettingsAsync(new AppSettings
        {
            RootFolder = _tempRoot,
            BillExportFolder = Path.Combine(_tempRoot, "Bills"),
            WorkshopName = "XƯỞNG IN VIỆT NAM",
            WorkshopSlogan = "Chuyên in ảnh kỷ yếu & album",
            WorkshopPhone = "0933.444.555",
            WorkshopAddress = "456 Phố In, Hà Nội",
            InvoiceFooterMessage = "Hân hạnh được phục vụ quý khách!",
            BankBinOrCode = "970422", // MB Bank
            BankAccountNumber = "0333888999",
            BankAccountName = "XUONG IN VIET NAM"
        });

        var bill = CreateSampleBill();
        string excelPath = await _excelExporter.ExportBillToExcelAsync(bill);

        File.Exists(excelPath).Should().BeTrue();

        using var wb = new XLWorkbook(excelPath);
        var ws = wb.Worksheets.Worksheet(1);

        // Header brand check
        string headerA1 = ws.Cell("A1").GetString();
        headerA1.Should().Contain("XƯỞNG IN VIỆT NAM");
        headerA1.Should().Contain("Chuyên in ảnh kỷ yếu & album");

        // Payment info row check
        bool foundPaymentRow = false;
        bool foundFooterRow = false;

        for (int r = 8; r <= 35; r++)
        {
            string rowText = ws.Cell(r, 1).GetString();
            if (rowText.Contains("Thông tin chuyển khoản:") && rowText.Contains("0333888999"))
            {
                foundPaymentRow = true;
                rowText.Should().Contain("XUONG IN VIET NAM");
                rowText.Should().Contain(bill.BillNumber);
            }
            if (rowText.Contains("Hân hạnh được phục vụ quý khách!"))
            {
                foundFooterRow = true;
            }
        }

        foundPaymentRow.Should().BeTrue("Bảng tính Excel cần có hàng thông tin chuyển khoản ngân hàng");
        foundFooterRow.Should().BeTrue("Bảng tính Excel cần có câu cảm ơn chân trang từ cài đặt");
    }

    [Fact]
    public async Task WpfShippingLabelExporter_RendersWithCustomWorkshopAndLogo()
    {
        string dummyLogo = CreateDummyPngImage(80, 40, "shipping_logo.png");

        await _settingsRepo.SaveSettingsAsync(new AppSettings
        {
            RootFolder = _tempRoot,
            BillExportFolder = Path.Combine(_tempRoot, "Bills"),
            WorkshopName = "XƯỞNG GIAO HÀNG NHANH",
            WorkshopPhone = "0944.555.666",
            WorkshopAddress = "Kho số 1 Cầu Giấy",
            WorkshopLogoPath = dummyLogo
        });

        var bill = CreateSampleBill();
        bill.ShippingAddressSnapshot = "Số 10 Nguyễn Trãi, Thanh Xuân, Hà Nội";

        string labelPath = await _shippingExporter.ExportShippingLabelImageAsync(bill);

        File.Exists(labelPath).Should().BeTrue();
        new FileInfo(labelPath).Length.Should().BeGreaterThan(3000);
    }

    [Fact]
    public async Task SettingsRepository_SavesAndLoads_CloudSyncSettings()
    {
        var settings = new AppSettings
        {
            RootFolder = _tempRoot,
            BillExportFolder = Path.Combine(_tempRoot, "Bills"),
            EnableCloudSync = true,
            CloudSyncApiUrl = "https://lalab.tinix.io.vn",
            CloudSyncSecret = "test-secret-key-12345"
        };

        await _settingsRepo.SaveSettingsAsync(settings);
        var loaded = await _settingsRepo.GetSettingsAsync();

        loaded.EnableCloudSync.Should().BeTrue();
        loaded.CloudSyncApiUrl.Should().Be("https://lalab.tinix.io.vn");
        loaded.CloudSyncSecret.Should().Be("test-secret-key-12345");
    }
}
