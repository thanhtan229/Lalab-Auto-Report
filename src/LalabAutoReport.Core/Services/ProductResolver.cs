using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LalabAutoReport.Core.Services;

public class ProductResolver : IProductResolver
{
    private static readonly Regex TrailingJobIndexRegex = new(
        @"(?:[\s_\-#(]*(?:bộ|bo|set|đơn|don|cuốn|cuon|tập|tap|bản|ban|đợt|dot|số|so|quyển|quyen|lần|lan|vol|volume)\s*(?:\d+|[a-zA-Z]\b|[ivxIVX]+\b)?|[\s_\-#(]+(?:\d+|[a-zA-Z]\b|[ivxIVX]+\b)|\d+)\s*\)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex LeadingJobIndexRegex = new(
        @"^[\s_\-#(]*(?:(?:bộ|bo|set|đơn|don|cuốn|cuon|tập|tap|bản|ban|đợt|dot|số|so|quyển|quyen|lần|lan|vol|volume)\s*(?:\d+|[a-zA-Z]\b|[ivxIVX]+\b)?|(?:\d+|[a-zA-Z]\b|[ivxIVX]+\b))[\s_\-#):]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IProductRepository _productRepository;
    private readonly ILogger<ProductResolver>? _logger;

    public ProductResolver(
        IProductRepository productRepository,
        ILogger<ProductResolver>? logger = null)
    {
        _productRepository = productRepository;
        _logger = logger;
    }

    public async Task<ProductResolutionResult> ResolveProductAsync(string folderName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return new ProductResolutionResult(
                Status: PrintSpecificationResolutionStatus.Unknown,
                ResolvedVariant: null,
                ResolvedFamily: null,
                CanonicalSize: null,
                Candidates: Array.Empty<ProductVariant>(),
                ErrorMessage: "Tên thư mục quy cách/sản phẩm rỗng."
            );
        }

        string normalizedInput = CustomerNormalizer.Normalize(folderName);

        // =========================================================================
        // PRIORITY 1: Exact Product-Specific Alias
        // =========================================================================
        var specificAliasMatches = await _productRepository.FindVariantsBySpecificAliasAsync(normalizedInput, cancellationToken);
        if (specificAliasMatches.Count == 1)
        {
            var matched = specificAliasMatches[0];
            return new ProductResolutionResult(
                Status: PrintSpecificationResolutionStatus.Resolved,
                ResolvedVariant: matched,
                ResolvedFamily: matched.Family,
                CanonicalSize: matched.CanonicalSize,
                Candidates: specificAliasMatches
            );
        }
        else if (specificAliasMatches.Count > 1)
        {
            _logger?.LogWarning("Exact alias collision: '{Folder}' maps to {Count} distinct variants.", folderName, specificAliasMatches.Count);
            return new ProductResolutionResult(
                Status: PrintSpecificationResolutionStatus.AmbiguousCollision,
                ResolvedVariant: null,
                ResolvedFamily: null,
                CanonicalSize: null,
                Candidates: specificAliasMatches,
                ErrorMessage: $"Trùng lặp: Alias '{folderName}' liên kết với nhiều sản phẩm khác nhau."
            );
        }

        // =========================================================================
        // PRIORITY 2: Family Alias + Size (Position independent: 'ab 20x30', '20x30 ab')
        // =========================================================================
        bool hasDimensions = SizeNormalizer.TryExtractDimensions(folderName, out string canonicalSize, out string remainingText, out _);

        if (hasDimensions && !string.IsNullOrWhiteSpace(remainingText))
        {
            string normRemainder = CustomerNormalizer.Normalize(remainingText);
            var allFamilies = await _productRepository.GetAllFamiliesAsync(cancellationToken);

            var matchingFamilies = new List<ProductFamily>();

            // Also check with trailing/leading job index stripped (e.g. "album - bo 2", "album - 1", "ab (2)", "in #1")
            string strippedRemainder = TrailingJobIndexRegex.Replace(normRemainder, "").Trim(' ', '_', '-', '.', '/', '(', ')');
            strippedRemainder = LeadingJobIndexRegex.Replace(strippedRemainder, "").Trim(' ', '_', '-', '.', '/', '(', ')');

            foreach (var family in allFamilies)
            {
                string normFamName = CustomerNormalizer.Normalize(family.Name);
                if (string.Equals(normFamName, normRemainder, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(strippedRemainder) && string.Equals(normFamName, strippedRemainder, StringComparison.OrdinalIgnoreCase)) ||
                    IsPrefixFamilyMatch(normFamName, normRemainder))
                {
                    matchingFamilies.Add(family);
                    continue;
                }

                // Check exact family aliases or prefix
                if (family.Aliases.Any(a => string.Equals(a.NormalizedAlias, normRemainder, StringComparison.OrdinalIgnoreCase) ||
                                           (!string.IsNullOrEmpty(strippedRemainder) && string.Equals(a.NormalizedAlias, strippedRemainder, StringComparison.OrdinalIgnoreCase)) ||
                                           IsPrefixFamilyMatch(a.NormalizedAlias, normRemainder)))
                {
                    matchingFamilies.Add(family);
                }
            }

            if (matchingFamilies.Count == 1)
            {
                var matchedFamily = matchingFamilies[0];
                var matchedVariant = matchedFamily.Variants
                    .FirstOrDefault(v => v.IsActive && SizeNormalizer.AreSizesEqual(v.CanonicalSize, canonicalSize));

                if (matchedVariant != null)
                {
                    return new ProductResolutionResult(
                        Status: PrintSpecificationResolutionStatus.Resolved,
                        ResolvedVariant: matchedVariant,
                        ResolvedFamily: matchedFamily,
                        CanonicalSize: canonicalSize,
                        Candidates: new[] { matchedVariant }
                    );
                }

                // Family recognized but variant size not registered
                return new ProductResolutionResult(
                    Status: PrintSpecificationResolutionStatus.Unknown,
                    ResolvedVariant: null,
                    ResolvedFamily: matchedFamily,
                    CanonicalSize: canonicalSize,
                    Candidates: Array.Empty<ProductVariant>(),
                    ErrorMessage: $"Dòng sản phẩm '{matchedFamily.Name}' chưa có kích thước '{canonicalSize}'."
                );
            }
            else if (matchingFamilies.Count > 1)
            {
                // Ambiguous between multiple families
                var candidates = matchingFamilies
                    .SelectMany(f => f.Variants.Where(v => v.IsActive && SizeNormalizer.AreSizesEqual(v.CanonicalSize, canonicalSize)))
                    .ToList();

                return new ProductResolutionResult(
                    Status: PrintSpecificationResolutionStatus.AmbiguousCollision,
                    ResolvedVariant: null,
                    ResolvedFamily: null,
                    CanonicalSize: canonicalSize,
                    Candidates: candidates,
                    ErrorMessage: $"Nhiều dòng sản phẩm cùng khớp alias '{remainingText}'."
                );
            }
        }

        // =========================================================================
        // AMBIGUITY CHECK: Pure dimension without Family Alias (Section 10)
        // If input is only size (e.g. "20x30") and multiple families share this size,
        // size alone cannot identify family -> NEEDS_REVIEW / AmbiguousCollision
        // =========================================================================
        var allVariants = await _productRepository.GetAllVariantsAsync(includeInactive: false, cancellationToken);

        if (hasDimensions && string.IsNullOrWhiteSpace(remainingText))
        {
            var variantsWithSize = allVariants
                .Where(v => SizeNormalizer.AreSizesEqual(v.CanonicalSize, canonicalSize))
                .ToList();

            var distinctFamilies = variantsWithSize.Select(v => v.FamilyId).Distinct().ToList();

            if (distinctFamilies.Count > 1)
            {
                _logger?.LogWarning("Size '{Size}' is ambiguous between {Count} product families.", canonicalSize, distinctFamilies.Count);
                return new ProductResolutionResult(
                    Status: PrintSpecificationResolutionStatus.AmbiguousCollision,
                    ResolvedVariant: null,
                    ResolvedFamily: null,
                    CanonicalSize: canonicalSize,
                    Candidates: variantsWithSize,
                    ErrorMessage: $"Kích thước '{canonicalSize}' trùng giữa nhiều dòng sản phẩm. Cần chọn sản phẩm cụ thể."
                );
            }
            else if (distinctFamilies.Count == 1 && variantsWithSize.Count == 1)
            {
                var v = variantsWithSize[0];
                if (v.Family != null && !string.Equals(v.Family.Name, "Ảnh in", StringComparison.OrdinalIgnoreCase))
                {
                    return new ProductResolutionResult(
                        Status: PrintSpecificationResolutionStatus.AmbiguousCollision,
                        ResolvedVariant: null,
                        ResolvedFamily: null,
                        CanonicalSize: canonicalSize,
                        Candidates: variantsWithSize,
                        ErrorMessage: $"Kích thước '{canonicalSize}' chưa rõ là '{v.Family.Name}' hay ảnh in thông thường. Cần chọn sản phẩm cụ thể."
                    );
                }

                return new ProductResolutionResult(
                    Status: PrintSpecificationResolutionStatus.Resolved,
                    ResolvedVariant: v,
                    ResolvedFamily: v.Family,
                    CanonicalSize: canonicalSize,
                    Candidates: variantsWithSize
                );
            }
        }

        // =========================================================================
        // PRIORITY 3: Exact canonical Product Name
        // =========================================================================
        var canonicalMatches = allVariants
            .Where(v => string.Equals(CustomerNormalizer.Normalize(v.CanonicalName), normalizedInput, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (canonicalMatches.Count == 1)
        {
            var matched = canonicalMatches[0];
            return new ProductResolutionResult(
                Status: PrintSpecificationResolutionStatus.Resolved,
                ResolvedVariant: matched,
                ResolvedFamily: matched.Family,
                CanonicalSize: matched.CanonicalSize,
                Candidates: canonicalMatches
            );
        }
        else if (canonicalMatches.Count > 1)
        {
            return new ProductResolutionResult(
                Status: PrintSpecificationResolutionStatus.AmbiguousCollision,
                ResolvedVariant: null,
                ResolvedFamily: null,
                CanonicalSize: null,
                Candidates: canonicalMatches,
                ErrorMessage: $"Nhiều sản phẩm có cùng tên '{folderName}'."
            );
        }


        return new ProductResolutionResult(
            Status: PrintSpecificationResolutionStatus.Unknown,
            ResolvedVariant: null,
            ResolvedFamily: null,
            CanonicalSize: null,
            Candidates: Array.Empty<ProductVariant>(),
            ErrorMessage: $"Chưa nhận diện quy cách in: '{folderName}'"
        );
    }

    public async Task<PrintSpecificationResolutionResult> ResolveSpecificationAsync(string folderName, CancellationToken cancellationToken = default)
    {
        var productResult = await ResolveProductAsync(folderName, cancellationToken);

        if (productResult.Status == PrintSpecificationResolutionStatus.Resolved && productResult.ResolvedVariant != null)
        {
            return new PrintSpecificationResolutionResult(
                Status: PrintSpecificationResolutionStatus.Resolved,
                ResolvedSpecification: productResult.ResolvedVariant.ToPrintSpecification()
            );
        }

        return new PrintSpecificationResolutionResult(
            Status: productResult.Status,
            ResolvedSpecification: null,
            ErrorMessage: productResult.ErrorMessage
        );
    }

    private static bool IsPrefixFamilyMatch(string familyToken, string text)
    {
        if (string.IsNullOrEmpty(familyToken) || string.IsNullOrEmpty(text))
            return false;

        if (text.StartsWith(familyToken, StringComparison.OrdinalIgnoreCase) && text.Length > familyToken.Length)
        {
            char nextChar = text[familyToken.Length];
            return nextChar is ' ' or '-' or '_' or '.' or '/' or '(' or ')' or '#';
        }

        return false;
    }
}
