using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LalabAutoReport.Core.Services;

public class PrintSpecificationResolver : IProductResolver
{
    private readonly IPrintSpecificationRepository _specificationRepository;
    private readonly IProductResolver? _productResolver;
    private readonly ILogger<PrintSpecificationResolver>? _logger;

    public PrintSpecificationResolver(
        IPrintSpecificationRepository specificationRepository,
        ILogger<PrintSpecificationResolver>? logger = null)
    {
        _specificationRepository = specificationRepository;
        _logger = logger;

        if (specificationRepository is IProductRepository prodRepo)
        {
            _productResolver = new ProductResolver(prodRepo);
        }
    }

    public async Task<ProductResolutionResult> ResolveProductAsync(string folderName, CancellationToken cancellationToken = default)
    {
        if (_productResolver != null)
        {
            return await _productResolver.ResolveProductAsync(folderName, cancellationToken);
        }

        var specRes = await ResolveSpecificationAsync(folderName, cancellationToken);
        var variant = specRes.ResolvedSpecification?.ToProductVariant();

        return new ProductResolutionResult(
            Status: specRes.Status,
            ResolvedVariant: variant,
            ResolvedFamily: null,
            CanonicalSize: variant?.CanonicalSize,
            Candidates: variant != null ? new[] { variant } : Array.Empty<ProductVariant>(),
            ErrorMessage: specRes.ErrorMessage
        );
    }

    public async Task<PrintSpecificationResolutionResult> ResolveSpecificationAsync(string folderName, CancellationToken cancellationToken = default)
    {
        if (_productResolver != null)
        {
            return await _productResolver.ResolveSpecificationAsync(folderName, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(folderName))
        {
            return new PrintSpecificationResolutionResult(PrintSpecificationResolutionStatus.Unknown, null, "Tên quy cách rỗng");
        }

        string normalized = CustomerNormalizer.Normalize(folderName);
        var allSpecs = await _specificationRepository.GetAllAsync(includeInactive: false, cancellationToken);

        var matches = new List<PrintSpecification>();

        foreach (var spec in allSpecs)
        {
            if (string.Equals(CustomerNormalizer.Normalize(spec.CanonicalName), normalized, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(spec);
                continue;
            }

            if (spec.Aliases.Any(a => string.Equals(a.NormalizedAlias, normalized, StringComparison.OrdinalIgnoreCase)))
            {
                matches.Add(spec);
            }
        }

        if (matches.Count == 1)
        {
            return new PrintSpecificationResolutionResult(
                Status: PrintSpecificationResolutionStatus.Resolved,
                ResolvedSpecification: matches[0]
            );
        }

        if (matches.Count > 1)
        {
            _logger?.LogWarning("Multiple specifications match folder '{Folder}'", folderName);
            return new PrintSpecificationResolutionResult(
                Status: PrintSpecificationResolutionStatus.AmbiguousCollision,
                ResolvedSpecification: null,
                ErrorMessage: $"Trùng lặp: Tên '{folderName}' khớp với nhiều quy cách khác nhau."
            );
        }

        return new PrintSpecificationResolutionResult(
            Status: PrintSpecificationResolutionStatus.Unknown,
            ResolvedSpecification: null,
            ErrorMessage: $"Chưa nhận diện quy cách in: '{folderName}'"
        );
    }
}
