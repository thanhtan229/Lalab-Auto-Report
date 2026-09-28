using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;

namespace LalabAutoReport.UI.ViewModels;

public partial class CustomersViewModel : ObservableObject
{
    private readonly ICustomerRepository _customerRepository;

    [ObservableProperty]
    private ObservableCollection<Customer> _allCustomers = new();

    [ObservableProperty]
    private ObservableCollection<Customer> _filteredCustomers = new();

    [ObservableProperty]
    private Customer? _selectedCustomer;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _newCustomerName = string.Empty;

    [ObservableProperty]
    private string _newAliasText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public CustomersViewModel(ICustomerRepository customerRepository)
    {
        _customerRepository = customerRepository;
    }

    public async Task LoadCustomersAsync()
    {
        var customers = await _customerRepository.GetAllAsync();
        AllCustomers.Clear();
        foreach (var c in customers)
        {
            AllCustomers.Add(c);
        }
        ApplyFilter();
        if (SelectedCustomer != null)
        {
            SelectedCustomer = AllCustomers.FirstOrDefault(c => c.Id == SelectedCustomer.Id) ?? AllCustomers.FirstOrDefault();
        }
        else
        {
            SelectedCustomer = AllCustomers.FirstOrDefault();
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var filtered = AllCustomers.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            string s = SearchText.Trim().ToLowerInvariant();
            filtered = filtered.Where(c =>
                c.CanonicalName.ToLowerInvariant().Contains(s) ||
                c.Aliases.Any(a => a.AliasText.ToLowerInvariant().Contains(s)));
        }

        FilteredCustomers.Clear();
        foreach (var c in filtered)
        {
            FilteredCustomers.Add(c);
        }
    }

    [RelayCommand]
    private async Task CreateCustomerAsync()
    {
        if (string.IsNullOrWhiteSpace(NewCustomerName))
        {
            StatusMessage = "Vui lòng nhập tên khách hàng!";
            return;
        }

        var customer = new Customer
        {
            CanonicalName = NewCustomerName.Trim()
        };

        var created = await _customerRepository.CreateCustomerAsync(customer);
        NewCustomerName = string.Empty;
        StatusMessage = $"Đã thêm khách hàng: {created.CanonicalName}";
        await LoadCustomersAsync();
        SelectedCustomer = AllCustomers.FirstOrDefault(c => c.Id == created.Id);
    }

    [RelayCommand]
    private async Task AddAliasAsync()
    {
        if (SelectedCustomer == null)
        {
            StatusMessage = "Vui lòng chọn khách hàng trước khi thêm alias!";
            return;
        }

        if (string.IsNullOrWhiteSpace(NewAliasText))
        {
            StatusMessage = "Vui lòng nhập tên alias!";
            return;
        }

        await _customerRepository.AddAliasAsync(SelectedCustomer.Id, NewAliasText.Trim());
        StatusMessage = $"Đã thêm alias '{NewAliasText.Trim()}' cho {SelectedCustomer.CanonicalName}";
        NewAliasText = string.Empty;
        await LoadCustomersAsync();
    }

    [RelayCommand]
    private async Task RemoveAliasAsync(CustomerAlias? alias)
    {
        if (alias == null || SelectedCustomer == null) return;

        await _customerRepository.RemoveAliasAsync(alias.Id);
        StatusMessage = $"Đã xóa alias '{alias.AliasText}'";
        await LoadCustomersAsync();
    }
}
