using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;

namespace LalabAutoReport.UI.ViewModels;

public partial class GlobalSearchViewModel : ObservableObject
{
    private readonly IGlobalSearchService _searchService;
    private readonly ICustomerBillRepository _customerBillRepository;
    private CancellationTokenSource? _searchCts;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private string _statusText = "Nhập từ khóa (mã đơn, số bill, tên khách, số điện thoại, thư mục...) để tìm kiếm";

    [ObservableProperty]
    private GlobalSearchResult? _selectedResult;

    public ObservableCollection<GlobalSearchResult> Results { get; } = new();

    public event Action<string, string?>? NavigateToOrderRequested;
    public event Action<CustomerBill>? OpenBillRequested;
    public event Action? RequestClose;

    public GlobalSearchViewModel(
        IGlobalSearchService searchService,
        ICustomerBillRepository customerBillRepository)
    {
        _searchService = searchService;
        _customerBillRepository = customerBillRepository;
    }

    async partial void OnSearchTextChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length < 1)
        {
            Results.Clear();
            SelectedResult = null;
            IsSearching = false;
            StatusText = "Nhập từ khóa (mã đơn, số bill, tên khách, số điện thoại, thư mục...) để tìm kiếm";
            return;
        }

        try
        {
            IsSearching = true;
            StatusText = "Đang tìm kiếm...";

            // Debounce 150ms for snappy typing feel
            await Task.Delay(150, token);

            var items = await _searchService.SearchAsync(value.Trim(), 50, token);

            if (token.IsCancellationRequested) return;

            Results.Clear();
            foreach (var item in items)
            {
                Results.Add(item);
            }

            SelectedResult = Results.Count > 0 ? Results[0] : null;
            StatusText = Results.Count > 0
                ? $"Tìm thấy {Results.Count} kết quả phù hợp"
                : "Không tìm thấy đơn hàng hoặc hóa đơn nào phù hợp.";
        }
        catch (OperationCanceledException)
        {
            // Normal debounce cancellation
        }
        catch (Exception ex)
        {
            StatusText = $"Lỗi tìm kiếm: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    public async Task OpenSelectedResultAsync()
    {
        if (SelectedResult == null) return;
        await OpenResultAsync(SelectedResult);
    }

    public async Task OpenResultAsync(GlobalSearchResult item)
    {
        if (item.ResultType == GlobalSearchResultType.Bill)
        {
            var bill = await _customerBillRepository.GetByIdAsync(item.Id);
            if (bill != null)
            {
                RequestClose?.Invoke();
                OpenBillRequested?.Invoke(bill);
            }
        }
        else if (item.ResultType == GlobalSearchResultType.Order)
        {
            RequestClose?.Invoke();
            NavigateToOrderRequested?.Invoke(item.Date, item.CodeOrNumber);
        }
    }

    [RelayCommand]
    public void OpenFolder(GlobalSearchResult? item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.FolderPath)) return;

        try
        {
            if (File.Exists(item.FolderPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{item.FolderPath}\"",
                    UseShellExecute = true
                });
            }
            else if (Directory.Exists(item.FolderPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = item.FolderPath,
                    UseShellExecute = true
                });
            }
        }
        catch { }
    }

    [RelayCommand]
    public void Close()
    {
        RequestClose?.Invoke();
    }
}
