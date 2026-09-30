using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using LalabAutoReport.Core.Services;

namespace LalabAutoReport.UI.ViewModels;

public record CustomerSuggestionItem(Customer Customer, double Score)
{
    public string DisplayText => $"👉 {Customer.CanonicalName} ({(int)(Score * 100)}% khớp)";
}

public partial class AssignCustomerViewModel : ObservableObject
{
    private readonly ICustomerRepository _customerRepository;
    private readonly IOrderRepository _orderRepository;
    private List<Customer> _allCustomers = new();

    public string FolderName { get; }
    public string WorkDate { get; }

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<Customer> _filteredCustomers = new();

    [ObservableProperty]
    private ObservableCollection<CustomerSuggestionItem> _suggestions = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmAssignCommand))]
    private Customer? _selectedCustomer;

    [ObservableProperty]
    private bool _saveAliasPermanently = true;

    [ObservableProperty]
    private string _newCustomerName = string.Empty;

    [ObservableProperty]
    private string _newCustomerPhone = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    // Results after dialog closes
    public Customer? AssignedCustomer { get; private set; }
    public bool IsGuestAssigned { get; private set; }
    public bool DialogResult { get; private set; }

    public event Action? RequestClose;

    public AssignCustomerViewModel(
        string folderName,
        string workDate,
        ICustomerRepository customerRepository,
        IOrderRepository orderRepository)
    {
        FolderName = folderName;
        WorkDate = workDate;
        _customerRepository = customerRepository;
        _orderRepository = orderRepository;
    }

    public async Task InitializeAsync()
    {
        IsBusy = true;
        try
        {
            var customers = await _customerRepository.GetAllAsync();
            _allCustomers = customers.ToList();

            ComputeSuggestions();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi tải khách hàng: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ComputeSuggestions()
    {
        Suggestions.Clear();
        if (string.IsNullOrWhiteSpace(FolderName) || _allCustomers.Count == 0) return;

        var matches = new List<CustomerSuggestionItem>();
        foreach (var cust in _allCustomers)
        {
            double maxScore = CustomerNormalizer.Similarity(FolderName, cust.CanonicalName);
            foreach (var alias in cust.Aliases)
            {
                double s = CustomerNormalizer.Similarity(FolderName, alias.AliasText);
                if (s > maxScore) maxScore = s;
            }

            if (maxScore >= 0.45)
            {
                matches.Add(new CustomerSuggestionItem(cust, maxScore));
            }
        }

        foreach (var item in matches.OrderByDescending(m => m.Score).Take(3))
        {
            Suggestions.Add(item);
        }

        // Auto select top suggestion if high confidence
        if (Suggestions.Count > 0 && Suggestions[0].Score >= 0.8)
        {
            SelectedCustomer = Suggestions[0].Customer;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var filtered = _allCustomers.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            string s = SearchText.Trim().ToLowerInvariant();
            filtered = filtered.Where(c =>
                c.CanonicalName.ToLowerInvariant().Contains(s) ||
                (!string.IsNullOrWhiteSpace(c.Phone) && c.Phone.Contains(s)) ||
                c.Aliases.Any(a => a.AliasText.ToLowerInvariant().Contains(s)));
        }

        FilteredCustomers.Clear();
        foreach (var cust in filtered.OrderBy(c => c.CanonicalName))
        {
            FilteredCustomers.Add(cust);
        }
    }

    [RelayCommand]
    private void SelectSuggestion(Customer? customer)
    {
        if (customer != null)
        {
            SelectedCustomer = customer;
        }
    }

    [RelayCommand]
    private async Task QuickCreateCustomerAsync()
    {
        if (string.IsNullOrWhiteSpace(NewCustomerName))
        {
            StatusMessage = "Vui lòng nhập tên khách hàng mới.";
            return;
        }

        IsBusy = true;
        try
        {
            var newCust = new Customer
            {
                CanonicalName = NewCustomerName.Trim(),
                Phone = !string.IsNullOrWhiteSpace(NewCustomerPhone) ? NewCustomerPhone.Trim() : null
            };

            var created = await _customerRepository.CreateCustomerAsync(newCust);
            _allCustomers.Add(created);
            NewCustomerName = string.Empty;
            NewCustomerPhone = string.Empty;
            SearchText = string.Empty;
            ApplyFilter();

            SelectedCustomer = created;
            StatusMessage = $"Đã tạo mới khách hàng '{created.CanonicalName}'.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi tạo khách hàng: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanConfirmAssign))]
    private async Task ConfirmAssignAsync()
    {
        if (SelectedCustomer == null) return;

        IsBusy = true;
        try
        {
            // 1. Alias handling if SaveAliasPermanently is checked
            if (SaveAliasPermanently)
            {
                var existingAlias = await _customerRepository.FindAliasByTextAsync(FolderName);
                if (existingAlias != null && existingAlias.CustomerId != SelectedCustomer.Id)
                {
                    var otherCustomer = await _customerRepository.GetByIdAsync(existingAlias.CustomerId);
                    string otherName = otherCustomer?.CanonicalName ?? "khách hàng khác";

                    var confirmResult = MessageBox.Show(
                        $"Biệt danh '{FolderName}' hiện đang thuộc về khách hàng '{otherName}'.\n\nBạn có muốn chuyển biệt danh này sang cho '{SelectedCustomer.CanonicalName}' không?",
                        "Xác nhận chuyển Biệt danh",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (confirmResult == MessageBoxResult.Yes)
                    {
                        await _customerRepository.ReassignAliasAsync(existingAlias.Id, SelectedCustomer.Id);
                    }
                    else
                    {
                        // User chose not to reassign alias, proceed with assigning order only
                    }
                }
                else if (existingAlias == null)
                {
                    await _customerRepository.AddAliasAsync(SelectedCustomer.Id, FolderName);
                }
            }

            // 2. Update orders in DB for this work date & folder
            await _orderRepository.UpdateFolderCustomerIdAsync(WorkDate, FolderName, SelectedCustomer.Id, null);

            AssignedCustomer = SelectedCustomer;
            IsGuestAssigned = false;
            DialogResult = true;
            RequestClose?.Invoke();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi khi gán khách hàng: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanConfirmAssign() => SelectedCustomer != null && !IsBusy;

    [RelayCommand]
    private async Task AssignAsGuestAsync()
    {
        IsBusy = true;
        try
        {
            // If alias was previously saved for this folder, remove it
            var existingAlias = await _customerRepository.FindAliasByTextAsync(FolderName);
            if (existingAlias != null)
            {
                await _customerRepository.RemoveAliasAsync(existingAlias.Id);
            }

            // Update orders to Guest (CustomerId = null, OrderName = "Khách lẻ")
            await _orderRepository.UpdateFolderCustomerIdAsync(WorkDate, FolderName, null, "Khách lẻ");

            AssignedCustomer = null;
            IsGuestAssigned = true;
            DialogResult = true;
            RequestClose?.Invoke();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi khi gán khách lẻ: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
        RequestClose?.Invoke();
    }
}
