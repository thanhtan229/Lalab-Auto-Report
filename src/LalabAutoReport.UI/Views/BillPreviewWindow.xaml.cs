using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Infrastructure.Reporting;

namespace LalabAutoReport.UI.Views;

public enum BillViewMode
{
    FitPage,    // Fit entire page in viewport without any scrollbars
    FitWidth,   // Fit container width (vertical scrolling)
    ActualSize, // 100% native 1080px resolution
    Custom      // Custom zoom level via Ctrl+Wheel
}

public partial class BillPreviewWindow : Window
{
    private readonly CustomerBill _bill;
    private IReadOnlyList<BitmapSource> _pages;
    private readonly IBillVisualRenderer? _renderer;
    private readonly Func<Task>? _onForceReexport;
    private int _currentPageIndex = 0;
    private BillViewMode _viewMode = BillViewMode.FitPage;
    private bool _canAutoCloseOnDeactivate = false;
    private bool _isOpeningChildDialog = false;
    private bool _isClosing = false;

    public BillPreviewWindow(CustomerBill bill, IReadOnlyList<BitmapSource> pages, IBillVisualRenderer? renderer = null, Func<Task>? onForceReexport = null)
    {
        InitializeComponent();

        _bill = bill ?? throw new ArgumentNullException(nameof(bill));
        _pages = pages ?? throw new ArgumentNullException(nameof(pages));
        _renderer = renderer;
        _onForceReexport = onForceReexport;

        btnForceReexport.Visibility = _onForceReexport != null ? Visibility.Visible : Visibility.Collapsed;

        if (_pages.Count == 0)
        {
            throw new ArgumentException("Danh sách ảnh hóa đơn không được rỗng.", nameof(pages));
        }

        Title = $"Xem Trước Hóa Đơn — {_bill.BillNumber} — {_bill.CustomerNameSnapshot}";
        txtTitle.Text = $"Hóa đơn: {_bill.BillNumber} — {_bill.CustomerNameSnapshot}";
        int totalQuantity = _bill.Lines.Where(l => l.IsIncluded).Sum(l => l.BilledQuantity);
        txtSubtitle.Text = $"Kỳ: {_bill.PeriodStart} → {_bill.PeriodEnd}  |  Tổng tiền: {_bill.GrandTotal:N0} đ  |  Số lượng: {totalQuantity} sp";

        if (_pages.Count > 1)
        {
            pnlPagination.Visibility = Visibility.Visible;
            btnCopyStitched.Visibility = Visibility.Visible;
        }
        else
        {
            pnlPagination.Visibility = Visibility.Collapsed;
            btnCopyStitched.Visibility = Visibility.Collapsed;
        }

        UpdateDisplayPage();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // 1. Calculate optimal window sizing according to work area (approx 88% of screen height)
        var workArea = SystemParameters.WorkArea;
        double targetHeight = Math.Min(960, Math.Max(560, workArea.Height * 0.88));
        double targetWidth = Math.Clamp(targetHeight * 0.78, 680, 860);

        Width = targetWidth;
        Height = targetHeight;

        // 2. Center window relative to Owner or Screen work area
        if (Owner != null && Owner.IsLoaded)
        {
            Left = Owner.Left + (Owner.Width - Width) / 2;
            Top = Owner.Top + (Owner.Height - Height) / 2;
        }
        else
        {
            Left = workArea.Left + (workArea.Width - Width) / 2;
            Top = workArea.Top + (workArea.Height - Height) / 2;
        }

        // Clamp inside screen bounds
        Left = Math.Max(workArea.Left, Math.Min(Left, workArea.Right - Width));
        Top = Math.Max(workArea.Top, Math.Min(Top, workArea.Bottom - Height));

        // 3. Apply default "Fit Page" mode so whole bill is 100% visible immediately without scrolling
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            SetViewMode(BillViewMode.FitPage);
            // Allow auto-close on deactivate once window is loaded and rendered
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() =>
            {
                _canAutoCloseOnDeactivate = true;
            }));
        }));
    }

    private void UpdateDisplayPage()
    {
        if (_currentPageIndex < 0) _currentPageIndex = 0;
        if (_currentPageIndex >= _pages.Count) _currentPageIndex = _pages.Count - 1;

        imgPreview.Source = _pages[_currentPageIndex];
        txtPageInfo.Text = $"Trang {_currentPageIndex + 1} / {_pages.Count}";
        btnPrevPage.IsEnabled = _currentPageIndex > 0;
        btnNextPage.IsEnabled = _currentPageIndex < _pages.Count - 1;

        if (_pages.Count > 1)
        {
            btnCopy.Content = $"📋 Sao chép Trang {_currentPageIndex + 1} (Ctrl+C)";
        }
        else
        {
            btnCopy.Content = "📋 Sao chép ảnh (Ctrl+C)";
        }

        ApplyCurrentViewMode();
    }

    private void SetViewMode(BillViewMode mode)
    {
        _viewMode = mode;

        radFitPage.IsChecked = (mode == BillViewMode.FitPage);
        radFitWidth.IsChecked = (mode == BillViewMode.FitWidth);
        radActualSize.IsChecked = (mode == BillViewMode.ActualSize);

        ApplyCurrentViewMode();
    }

    private void ApplyCurrentViewMode()
    {
        if (_pages == null || _currentPageIndex >= _pages.Count) return;
        var bmp = _pages[_currentPageIndex];
        if (bmp == null || bmp.PixelWidth <= 0 || bmp.PixelHeight <= 0) return;

        switch (_viewMode)
        {
            case BillViewMode.FitPage:
                ApplyFitPage(bmp);
                break;
            case BillViewMode.FitWidth:
                ApplyFitWidth(bmp);
                break;
            case BillViewMode.ActualSize:
                ApplyActualSize(bmp);
                break;
        }
    }

    private void ApplyFitPage(BitmapSource bmp)
    {
        scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;

        // Container dimensions subtracting border margins
        double availW = Math.Max(50, previewContainer.ActualWidth - 28);
        double availH = Math.Max(50, previewContainer.ActualHeight - 28);

        double scaleX = availW / bmp.PixelWidth;
        double scaleY = availH / bmp.PixelHeight;
        double scale = Math.Min(scaleX, scaleY);

        double targetW = Math.Floor(bmp.PixelWidth * scale);
        double targetH = Math.Floor(bmp.PixelHeight * scale);

        borderImagePaper.Width = targetW;
        borderImagePaper.Height = targetH;
        borderImagePaper.HorizontalAlignment = HorizontalAlignment.Center;
        borderImagePaper.VerticalAlignment = VerticalAlignment.Center;
        borderImagePaper.Margin = new Thickness(0);

        imgPreview.Width = targetW;
        imgPreview.Height = targetH;
    }

    private void ApplyFitWidth(BitmapSource bmp)
    {
        scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;

        // Reserve space for vertical scrollbar (~18px)
        double availW = Math.Max(50, previewContainer.ActualWidth - 28 - 18);
        double scale = availW / bmp.PixelWidth;
        double targetH = Math.Floor(bmp.PixelHeight * scale);

        borderImagePaper.Width = availW;
        borderImagePaper.Height = targetH;
        borderImagePaper.HorizontalAlignment = HorizontalAlignment.Center;
        borderImagePaper.VerticalAlignment = VerticalAlignment.Top;
        borderImagePaper.Margin = new Thickness(0, 8, 0, 16);

        imgPreview.Width = availW;
        imgPreview.Height = targetH;
    }

    private void ApplyActualSize(BitmapSource bmp)
    {
        scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;

        borderImagePaper.Width = bmp.PixelWidth;
        borderImagePaper.Height = bmp.PixelHeight;
        borderImagePaper.HorizontalAlignment = HorizontalAlignment.Center;
        borderImagePaper.VerticalAlignment = VerticalAlignment.Top;
        borderImagePaper.Margin = new Thickness(16);

        imgPreview.Width = bmp.PixelWidth;
        imgPreview.Height = bmp.PixelHeight;
    }

    private void PreviewContainer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_viewMode == BillViewMode.FitPage || _viewMode == BillViewMode.FitWidth)
        {
            ApplyCurrentViewMode();
        }
    }

    private void FitPage_Click(object sender, RoutedEventArgs e)
    {
        SetViewMode(BillViewMode.FitPage);
    }

    private void FitWidth_Click(object sender, RoutedEventArgs e)
    {
        SetViewMode(BillViewMode.FitWidth);
    }

    private void ActualSize_Click(object sender, RoutedEventArgs e)
    {
        SetViewMode(BillViewMode.ActualSize);
    }

    private void BorderImagePaper_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            // Toggle between FitPage and FitWidth on double-click
            if (_viewMode == BillViewMode.FitPage)
            {
                SetViewMode(BillViewMode.FitWidth);
            }
            else
            {
                SetViewMode(BillViewMode.FitPage);
            }
            e.Handled = true;
        }
    }

    private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            e.Handled = true;
            if (_pages == null || _currentPageIndex >= _pages.Count) return;
            var bmp = _pages[_currentPageIndex];
            if (bmp == null) return;

            double delta = e.Delta > 0 ? 1.15 : 0.85;
            double currentW = borderImagePaper.Width > 0 ? borderImagePaper.Width : bmp.PixelWidth;
            double newW = Math.Clamp(currentW * delta, 250, 3200);
            double scale = newW / bmp.PixelWidth;

            _viewMode = BillViewMode.Custom;
            radFitPage.IsChecked = false;
            radFitWidth.IsChecked = false;
            radActualSize.IsChecked = false;

            scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;

            borderImagePaper.Width = newW;
            borderImagePaper.Height = Math.Floor(bmp.PixelHeight * scale);
            borderImagePaper.HorizontalAlignment = HorizontalAlignment.Center;
            borderImagePaper.VerticalAlignment = VerticalAlignment.Top;
            borderImagePaper.Margin = new Thickness(16);

            imgPreview.Width = borderImagePaper.Width;
            imgPreview.Height = borderImagePaper.Height;
        }
    }

    private void PrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPageIndex > 0)
        {
            _currentPageIndex--;
            UpdateDisplayPage();
        }
    }

    private void NextPage_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPageIndex < _pages.Count - 1)
        {
            _currentPageIndex++;
            UpdateDisplayPage();
        }
    }

    private void CopyCurrentPage_Click(object sender, RoutedEventArgs e)
    {
        CopyBitmapToClipboard(_pages[_currentPageIndex], isStitched: false);
    }

    private void CopyStitched_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BitmapSource stitched;
            if (_renderer != null)
            {
                stitched = _renderer.StitchBitmapsVertically(_pages);
            }
            else
            {
                stitched = _pages[0];
            }

            CopyBitmapToClipboard(stitched, isStitched: true);
        }
        catch (Exception ex)
        {
            ShowChildMessageBox($"Lỗi ghép ảnh: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CopyBitmapToClipboard(BitmapSource bitmap, bool isStitched)
    {
        try
        {
            var dataObject = new DataObject();
            dataObject.SetImage(bitmap);
            Clipboard.SetDataObject(dataObject, true);

            string pageText = isStitched
                ? "Ghép toàn bộ hóa đơn"
                : (_pages.Count > 1 ? $"Trang {_currentPageIndex + 1}" : "Hóa đơn");

            txtStatus.Text = $"✓ Đã sao chép [{pageText}] vào Clipboard! Bạn có thể dán (Ctrl+V) ngay sang Zalo/Messenger.";
            txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(5, 150, 105)); // emerald green
            txtStatus.FontWeight = FontWeights.Bold;
        }
        catch (Exception ex)
        {
            ShowChildMessageBox($"Không thể sao chép vào Clipboard: {ex.Message}\n(Có thể do một ứng dụng khác đang chiếm giữ Clipboard, vui lòng thử lại)", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public void UpdatePages(IReadOnlyList<BitmapSource> newPages, CustomerBill? updatedBill = null)
    {
        if (newPages == null || newPages.Count == 0) return;
        _pages = newPages;

        var bill = updatedBill ?? _bill;
        int totalQuantity = bill.Lines.Where(l => l.IsIncluded).Sum(l => l.BilledQuantity);
        txtSubtitle.Text = $"Kỳ: {bill.PeriodStart} → {bill.PeriodEnd}  |  Tổng tiền: {bill.GrandTotal:N0} đ  |  Số lượng: {totalQuantity} sp";

        if (_pages.Count > 1)
        {
            pnlPagination.Visibility = Visibility.Visible;
            btnCopyStitched.Visibility = Visibility.Visible;
        }
        else
        {
            pnlPagination.Visibility = Visibility.Collapsed;
            btnCopyStitched.Visibility = Visibility.Collapsed;
        }

        UpdateDisplayPage();
    }

    private async void ForceReexport_Click(object sender, RoutedEventArgs e)
    {
        if (_onForceReexport == null) return;

        try
        {
            _isOpeningChildDialog = true;
            btnForceReexport.IsEnabled = false;
            txtStatus.Text = "⏳ Đang buộc xuất lại hóa đơn (JPEG + Excel)...";
            txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(217, 119, 6));
            txtStatus.FontWeight = FontWeights.SemiBold;

            await _onForceReexport();

            txtStatus.Text = "✓ Đã buộc xuất lại hóa đơn thành công và sao chép vào Clipboard!";
            txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(5, 150, 105));
            txtStatus.FontWeight = FontWeights.Bold;
        }
        catch (Exception ex)
        {
            ShowChildMessageBox($"Lỗi khi buộc xuất lại hóa đơn: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            txtStatus.Text = $"Lỗi xuất lại: {ex.Message}";
            txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
        }
        finally
        {
            btnForceReexport.IsEnabled = true;
            _isOpeningChildDialog = false;
        }
    }

    private async void SaveImage_Click(object sender, RoutedEventArgs e)
    {
        string customerSlug = WpfJpegBillExporter.SanitizeSlug(_bill.CustomerNameSnapshot);
        string defaultName = _pages.Count > 1
            ? $"{_bill.BillNumber}_{customerSlug}_Trang{_currentPageIndex + 1}.jpg"
            : $"{_bill.BillNumber}_{customerSlug}.jpg";

        var saveDialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "JPEG Image (*.jpg)|*.jpg|All Files (*.*)|*.*",
            FileName = defaultName
        };

        bool? result = false;
        try
        {
            _isOpeningChildDialog = true;
            result = saveDialog.ShowDialog(this);
        }
        finally
        {
            _isOpeningChildDialog = false;
        }

        if (result == true)
        {
            try
            {
                var currentBitmap = _pages[_currentPageIndex];
                if (_renderer != null)
                {
                    await _renderer.SaveBitmapToJpegAsync(currentBitmap, saveDialog.FileName);
                }
                else
                {
                    WpfJpegBillExporter.SaveBitmapToJpeg(currentBitmap, saveDialog.FileName);
                }

                txtStatus.Text = $"✓ Đã lưu ảnh thành công: {Path.GetFileName(saveDialog.FileName)}";
                txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(5, 150, 105));
                txtStatus.FontWeight = FontWeights.Bold;
            }
            catch (Exception ex)
            {
                ShowChildMessageBox($"Không thể lưu file ảnh: {ex.Message}", "Lỗi lưu file", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var printDialog = new System.Windows.Controls.PrintDialog();
            bool? result = false;
            try
            {
                _isOpeningChildDialog = true;
                result = printDialog.ShowDialog();
            }
            finally
            {
                _isOpeningChildDialog = false;
            }

            if (result == true)
            {
                var currentBitmap = _pages[_currentPageIndex];
                double printableWidth = printDialog.PrintableAreaWidth;
                double printableHeight = printableWidth * (currentBitmap.PixelHeight / (double)currentBitmap.PixelWidth);

                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawImage(currentBitmap, new Rect(0, 0, printableWidth, printableHeight));
                }

                printDialog.PrintVisual(visual, $"HoaDon_{_bill.BillNumber}_Trang{_currentPageIndex + 1}");
                txtStatus.Text = $"✓ Đã gửi lệnh in Trang {_currentPageIndex + 1} tới máy in thành công.";
                txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(5, 150, 105));
            }
        }
        catch (Exception ex)
        {
            ShowChildMessageBox($"Không thể in: {ex.Message}", "Lỗi in ấn", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private MessageBoxResult ShowChildMessageBox(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
    {
        try
        {
            _isOpeningChildDialog = true;
            return MessageBox.Show(this, messageBoxText, caption, button, icon);
        }
        finally
        {
            _isOpeningChildDialog = false;
        }
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (_canAutoCloseOnDeactivate && !_isOpeningChildDialog && !_isClosing)
        {
            _isClosing = true;
            Close();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _isClosing = true;
        base.OnClosed(e);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (!_isClosing)
        {
            _isClosing = true;
            Close();
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (!_isClosing)
            {
                _isClosing = true;
                Close();
            }
            e.Handled = true;
        }
        else if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            CopyCurrentPage_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Left && _pages.Count > 1)
        {
            PrevPage_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Right && _pages.Count > 1)
        {
            NextPage_Click(sender, e);
            e.Handled = true;
        }
    }
}
