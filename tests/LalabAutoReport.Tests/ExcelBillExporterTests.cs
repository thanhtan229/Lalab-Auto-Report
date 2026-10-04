using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ClosedXML.Excel;
using FluentAssertions;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using LalabAutoReport.Infrastructure.Data;
using LalabAutoReport.Infrastructure.Reporting;
using Xunit;

namespace LalabAutoReport.Tests;

public class ExcelBillExporterTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly SqliteSettingsRepository _settingsRepo;
    private readonly ClosedXmlBillExporter _excelExporter;
    private readonly WpfJpegBillExporter _jpegExporter;

    public ExcelBillExporterTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "Lalab_ExcelTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        string dbPath = Path.Combine(_tempRoot, "test_settings.db");
        var connectionFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connectionFactory);
        migrator.MigrateAsync().GetAwaiter().GetResult();

        _settingsRepo = new SqliteSettingsRepository(connectionFactory);
        _settingsRepo.SaveSettingsAsync(new AppSettings
        {
            RootFolder = _tempRoot,
            BillExportFolder = Path.Combine(_tempRoot, "Bills"),
            SupportedExtensions = new() { ".jpg", ".jpeg", ".png" },
            WorkshopName = "LALAB PHOTO WORKSHOP"
        }).GetAwaiter().GetResult();

        _excelExporter = new ClosedXmlBillExporter(_settingsRepo);
        _jpegExporter = new WpfJpegBillExporter(_settingsRepo);
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

    [Fact]
    public void DeterministicFileName_MatchesJpegFileNameBase()
    {
        var bill = new CustomerBill
        {
            BillNumber = "BILL-20260929-001",
            CustomerNameSnapshot = "Nguyễn Văn An (Studio X)"
        };

        string jpegName = WpfJpegBillExporter.GenerateDeterministicFileName(bill);
        string excelName = ClosedXmlBillExporter.GenerateDeterministicFileName(bill);

        Path.GetFileNameWithoutExtension(jpegName).Should().Be(Path.GetFileNameWithoutExtension(excelName));
        Path.GetExtension(jpegName).Should().Be(".jpg");
        Path.GetExtension(excelName).Should().Be(".xlsx");
    }

    [Fact]
    public async Task ExportBillToExcelAsync_CreatesValidSpreadsheet_WithFormulasAndDetails()
    {
        var bill = new CustomerBill
        {
            Id = 1,
            BillNumber = "BILL-20260929-002",
            CustomerNameSnapshot = "Studio Mai Vàng",
            PhoneSnapshot = "0901234567",
            PeriodStart = "2026-09-20",
            PeriodEnd = "2026-09-29",
            ProductSubtotal = 1100000,
            GrandTotal = 1080000,
            Note = "Giao hàng trước 17h chiều",
            CreatedAt = DateTimeOffset.UtcNow,
            Orders = new List<CustomerBillOrder>
            {
                new CustomerBillOrder
                {
                    OrderNameSnapshot = "Đơn tiệc cưới",
                    OrderDateSnapshot = "2026-09-25",
                    IsIncluded = true,
                    Subtotal = 600000,
                    Lines = new List<CustomerBillLine>
                    {
                        new CustomerBillLine
                        {
                            ProductNameSnapshot = "Ảnh in 13x18 Lụa",
                            VariantSnapshot = "Giấy lụa cao cấp",
                            BilledQuantity = 200,
                            BilledUnitPrice = 3000,
                            LineTotal = 600000,
                            IsIncluded = true,
                            BillingMethodSnapshot = BillingMethod.FileCount,
                            Note = "Chỉnh màu sáng"
                        }
                    }
                },
                new CustomerBillOrder
                {
                    OrderNameSnapshot = "Album phóng",
                    OrderDateSnapshot = "2026-09-28",
                    IsIncluded = true,
                    Subtotal = 500000,
                    Lines = new List<CustomerBillLine>
                    {
                        new CustomerBillLine
                        {
                            ProductNameSnapshot = "Album Mở Phẳng 20x30",
                            VariantSnapshot = "Bìa Da",
                            BilledQuantity = 1,
                            SheetCount = 15,
                            IncludedSheetsSnapshot = 10,
                            BasePriceSnapshot = 400000,
                            ExtraSheetCount = 5,
                            ExtraSheetPriceSnapshot = 20000,
                            LineTotal = 500000,
                            IsIncluded = true,
                            BillingMethodSnapshot = BillingMethod.AlbumBasePlusExtra,
                            Note = "Bìa màu nâu"
                        }
                    }
                }
            },
            Adjustments = new List<BillAdjustment>
            {
                new BillAdjustment
                {
                    Label = "Chiết khấu khách quen",
                    Amount = 50000,
                    Direction = AdjustmentDirection.Deduct
                },
                new BillAdjustment
                {
                    Label = "Phí ship hỏa tốc",
                    Amount = 30000,
                    Direction = AdjustmentDirection.Add
                }
            }
        };

        // Act
        string excelPath = await _excelExporter.ExportBillToExcelAsync(bill);

        // Assert
        File.Exists(excelPath).Should().BeTrue();
        new FileInfo(excelPath).Length.Should().BeGreaterThan(0);

        // Verify with ClosedXML reader
        using var wb = new XLWorkbook(excelPath);
        wb.Worksheets.Count.Should().BeGreaterThan(0);

        var ws = wb.Worksheets.Worksheet(1);
        ws.Cell("A1").GetString().Should().NotBeNullOrWhiteSpace();
        ws.Cell("A2").GetString().Should().Be("HÓA ĐƠN BÁN HÀNG");
        ws.Cell("A2").Style.Alignment.Horizontal.Should().Be(XLAlignmentHorizontalValues.Center);
        ws.Cell("B4").GetString().Should().Be("BILL-20260929-002");
        ws.Cell("B5").GetString().Should().Contain("Studio Mai Vàng");

        // Check line items exist
        bool foundPhotoLine = false;
        bool foundAlbumLine = false;
        for (int r = 8; r <= 20; r++)
        {
            string productVal = ws.Cell(r, 3).GetString();
            if (productVal.Contains("Ảnh in 13x18 Lụa"))
            {
                foundPhotoLine = true;
                ws.Cell(r, 5).GetDouble().Should().Be(200); // SL
                ws.Cell(r, 8).HasFormula.Should().BeTrue(); // Formula =E{r}*G{r}
            }
            if (productVal.Contains("Album"))
            {
                foundAlbumLine = true;
                ws.Cell(r, 4).GetString().Should().Contain("Gói chuẩn 10 tờ");
                ws.Cell(r, 4).GetString().Should().Contain("5 tờ phát sinh");
                ws.Cell(r, 8).GetDouble().Should().Be(500000);
            }
        }

        foundPhotoLine.Should().BeTrue();
        foundAlbumLine.Should().BeTrue();
    }

    [Fact]
    public async Task DualExport_ExportsBothJpegAndExcel_WithSameBaseName_InSameFolder()
    {
        var bill = new CustomerBill
        {
            Id = 42,
            BillNumber = "BILL-20260929-999",
            CustomerNameSnapshot = "Anh Hoàng Pro",
            ProductSubtotal = 350000,
            GrandTotal = 350000,
            CreatedAt = DateTimeOffset.UtcNow,
            Orders = new List<CustomerBillOrder>
            {
                new CustomerBillOrder
                {
                    OrderNameSnapshot = "Đơn 1",
                    OrderDateSnapshot = "2026-09-29",
                    IsIncluded = true,
                    Subtotal = 350000,
                    Lines = new List<CustomerBillLine>
                    {
                        new CustomerBillLine
                        {
                            ProductNameSnapshot = "Ảnh ép gỗ 20x30",
                            BilledQuantity = 5,
                            BilledUnitPrice = 70000,
                            LineTotal = 350000,
                            IsIncluded = true,
                            BillingMethodSnapshot = BillingMethod.FileCount
                        }
                    }
                }
            }
        };

        // Export both
        string jpegPath = await _jpegExporter.ExportBillToJpegAsync(bill);
        string excelPath = await _excelExporter.ExportBillToExcelAsync(bill);

        // Verify both files exist
        File.Exists(jpegPath).Should().BeTrue();
        File.Exists(excelPath).Should().BeTrue();

        // Verify same folder
        Path.GetDirectoryName(jpegPath).Should().Be(Path.GetDirectoryName(excelPath));

        // Verify same base filename
        Path.GetFileNameWithoutExtension(jpegPath).Should().Be(Path.GetFileNameWithoutExtension(excelPath));

        // Verify extensions
        Path.GetExtension(jpegPath).ToLowerInvariant().Should().Be(".jpg");
        Path.GetExtension(excelPath).ToLowerInvariant().Should().Be(".xlsx");

        // Verify file sizes
        new FileInfo(jpegPath).Length.Should().BeGreaterThan(1000);
        new FileInfo(excelPath).Length.Should().BeGreaterThan(1000);
    }
}
