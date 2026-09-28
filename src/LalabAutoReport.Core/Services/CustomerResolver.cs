using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LalabAutoReport.Core.Services;

public class CustomerResolver : ICustomerResolver
{
    private readonly ICustomerRepository _customerRepository;
    private readonly ILogger<CustomerResolver>? _logger;

    public CustomerResolver(ICustomerRepository customerRepository, ILogger<CustomerResolver>? logger = null)
    {
        _customerRepository = customerRepository;
        _logger = logger;
    }

    public async Task<CustomerResolutionResult> ResolveCustomerAsync(string folderName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return new CustomerResolutionResult(CustomerResolutionStatus.Unresolved, null, Array.Empty<Customer>());
        }

        string normalized = CustomerNormalizer.Normalize(folderName);

        // 1. Check all aliases and canonical names
        var allCustomers = await _customerRepository.GetAllAsync(cancellationToken);

        var exactMatches = new List<Customer>();
        foreach (var customer in allCustomers)
        {
            // Check canonical name
            if (string.Equals(CustomerNormalizer.Normalize(customer.CanonicalName), normalized, StringComparison.OrdinalIgnoreCase))
            {
                exactMatches.Add(customer);
                continue;
            }

            // Check aliases
            if (customer.Aliases.Any(a => string.Equals(a.NormalizedAlias, normalized, StringComparison.OrdinalIgnoreCase)))
            {
                exactMatches.Add(customer);
            }
        }

        // Single exact match -> auto map!
        if (exactMatches.Count == 1)
        {
            var match = exactMatches[0];
            bool isExactText = string.Equals(match.CanonicalName, folderName, StringComparison.Ordinal) ||
                               match.Aliases.Any(a => string.Equals(a.AliasText, folderName, StringComparison.Ordinal));

            return new CustomerResolutionResult(
                Status: isExactText ? CustomerResolutionStatus.ExactMatch : CustomerResolutionStatus.NormalizedMatch,
                ResolvedCustomer: match,
                SuggestedCustomers: Array.Empty<Customer>()
            );
        }

        // Collision: multiple customers claim this normalized alias!
        if (exactMatches.Count > 1)
        {
            _logger?.LogWarning("Alias collision: normalized folder '{Norm}' matches {Count} customers.", normalized, exactMatches.Count);
            return new CustomerResolutionResult(
                Status: CustomerResolutionStatus.AmbiguousCollision,
                ResolvedCustomer: null,
                SuggestedCustomers: exactMatches,
                ErrorMessage: $"Trùng lặp: Tên thư mục '{folderName}' khớp với {exactMatches.Count} khách hàng khác nhau."
            );
        }

        // 2. No exact match -> Calculate fuzzy suggestions (NEVER silently merge!)
        var suggestions = new List<(Customer Customer, double Score)>();

        foreach (var customer in allCustomers)
        {
            double maxScore = CustomerNormalizer.Similarity(folderName, customer.CanonicalName);

            foreach (var alias in customer.Aliases)
            {
                double score = CustomerNormalizer.Similarity(folderName, alias.AliasText);
                if (score > maxScore)
                {
                    maxScore = score;
                }
            }

            if (maxScore >= 0.5) // Minimum similarity threshold for suggestion
            {
                suggestions.Add((customer, maxScore));
            }
        }

        if (suggestions.Count > 0)
        {
            var topSuggestions = suggestions
                .OrderByDescending(s => s.Score)
                .Take(5)
                .Select(s => s.Customer)
                .ToList();

            return new CustomerResolutionResult(
                Status: CustomerResolutionStatus.FuzzySuggested,
                ResolvedCustomer: null, // Advisory only! Never silently auto-merge!
                SuggestedCustomers: topSuggestions
            );
        }

        // 3. Completely unresolved
        return new CustomerResolutionResult(
            Status: CustomerResolutionStatus.Unresolved,
            ResolvedCustomer: null,
            SuggestedCustomers: Array.Empty<Customer>()
        );
    }
}
