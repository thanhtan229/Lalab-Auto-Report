using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LalabAutoReport.Core.Services;

public class FolderStructureParser : IFolderStructureParser
{
    private static readonly Regex DatePatternRegex = new(@"^\d{4}[-_.]\d{2}[-_.]\d{2}$", RegexOptions.Compiled);
    private static readonly Regex DimensionRegex = new(@"^\d+[\s._xX*xX-]+\d+", RegexOptions.Compiled);

    private readonly IFileSystemAdapter _fileSystem;
    private readonly IPrintSpecificationRepository? _specRepository;
    private readonly IProductResolver? _productResolver;
    private readonly Func<string, bool>? _isProductPredicate;
    private readonly ILogger<FolderStructureParser>? _logger;

    public FolderStructureParser(
        IFileSystemAdapter fileSystem,
        IPrintSpecificationRepository? specRepository = null,
        ILogger<FolderStructureParser>? logger = null,
        Func<string, bool>? isProductPredicate = null,
        IProductResolver? productResolver = null)
    {
        _fileSystem = fileSystem;
        _specRepository = specRepository;
        _logger = logger;
        _isProductPredicate = isProductPredicate;
        _productResolver = productResolver ?? (specRepository is IProductRepository prodRepo ? new ProductResolver(prodRepo) : null);
    }

    public IReadOnlyList<string> DiscoverDateFolders(string rootFolder)
    {
        if (string.IsNullOrWhiteSpace(rootFolder) || !_fileSystem.DirectoryExists(rootFolder))
        {
            _logger?.LogWarning("Root folder '{Root}' does not exist or is empty.", rootFolder);
            return Array.Empty<string>();
        }

        var results = new List<string>();
        try
        {
            foreach (var dir in _fileSystem.EnumerateDirectories(rootFolder))
            {
                string folderName = _fileSystem.GetFileName(dir);
                if (DatePatternRegex.IsMatch(folderName))
                {
                    results.Add(folderName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to enumerate date folders in '{Root}'", rootFolder);
        }

        return results.OrderByDescending(d => d).ToList();
    }

    public IReadOnlyList<DiscoveredOrder> DiscoverOrdersForDate(string rootFolder, string dateString)
    {
        string dateFolderPath = _fileSystem.Combine(rootFolder, dateString);
        if (!_fileSystem.DirectoryExists(dateFolderPath))
        {
            _logger?.LogWarning("Date folder '{Path}' does not exist.", dateFolderPath);
            throw new System.IO.DirectoryNotFoundException($"Không đọc được thư mục ngày: {dateFolderPath}");
        }

        var orders = new List<DiscoveredOrder>();
        try
        {
            foreach (var customerDir in _fileSystem.EnumerateDirectories(dateFolderPath))
            {
                try { orders.AddRange(DiscoverOrdersInFolder(rootFolder, customerDir, dateString)); }
                catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
                {
                    orders.Add(new DiscoveredOrder(dateString, _fileSystem.GetFileName(customerDir), customerDir,
                        _fileSystem.GetRelativePath(rootFolder, customerDir), Array.Empty<DiscoveredSpecification>(),
                        DiscoveryError: $"{customerDir}: {ex.Message}"));
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to enumerate customer folders in date '{DatePath}'", dateFolderPath);
            throw;
        }

        return orders.OrderBy(o => o.OriginalCustomerFolderName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(o => o.OrderName, StringComparer.OrdinalIgnoreCase)
                     .ToList();
    }

    public IReadOnlyList<DiscoveredOrder> DiscoverOrdersInFolder(string rootFolder, string folderPath, string? dateString = null)
    {
        if (!_fileSystem.DirectoryExists(folderPath))
        {
            throw new System.IO.DirectoryNotFoundException($"Không đọc được thư mục khách: {folderPath}");
        }

        string customerFolderName = _fileSystem.GetFileName(folderPath);
        string relativeCustomerPath = _fileSystem.GetRelativePath(rootFolder, folderPath);
        dateString ??= DateTime.UtcNow.ToString("yyyy-MM-dd");

        string normalizedRel = relativeCustomerPath.Replace('/', '\\').Trim('\\');
        string[] parts = normalizedRel.Split('\\', StringSplitOptions.RemoveEmptyEntries);

        // Check if folderPath is a deep descendant inside a date folder: Date \ Customer \ ...
        if (parts.Length >= 3 && DatePatternRegex.IsMatch(parts[0]))
        {
            string date = parts[0];
            string customerName = parts[1];

            // Subcase: KHACH_LE
            if (string.Equals(customerName, "KHACH_LE", StringComparison.OrdinalIgnoreCase))
            {
                string guestName = parts[2];
                string guestRelPath = _fileSystem.Combine(date, customerName, guestName);
                string guestFullPath = _fileSystem.Combine(rootFolder, guestRelPath);

                if (parts.Length == 3)
                {
                    var specs = DiscoverSpecificationsInFolder(rootFolder, guestFullPath);
                    return new[]
                    {
                        new DiscoveredOrder(
                            Date: date,
                            OriginalCustomerFolderName: guestName,
                            FullPath: guestFullPath,
                            RelativePath: guestRelPath,
                            Specifications: specs,
                            Kind: OrderKind.Explicit,
                            OrderName: guestName
                        )
                    };
                }
                else
                {
                    string specName = parts[3];
                    string specRelPath = _fileSystem.Combine(guestRelPath, specName);
                    string specFullPath = _fileSystem.Combine(rootFolder, specRelPath);
                    return new[]
                    {
                        new DiscoveredOrder(
                            Date: date,
                            OriginalCustomerFolderName: guestName,
                            FullPath: guestFullPath,
                            RelativePath: guestRelPath,
                            Specifications: new[] { new DiscoveredSpecification(specName, specFullPath, specRelPath) },
                            Kind: OrderKind.Explicit,
                            OrderName: guestName
                        )
                    };
                }
            }

            // Normal customer: Check if parts[2] is an explicit order or a direct product specification folder
            bool isExplicitOrder = (parts.Length >= 4 && IsProductFolder(parts[3]));
            if (!isExplicitOrder)
            {
                string potentialOrderPath = _fileSystem.Combine(rootFolder, date, customerName, parts[2]);
                if (_fileSystem.DirectoryExists(potentialOrderPath))
                {
                    try
                    {
                        var subs = _fileSystem.EnumerateDirectories(potentialOrderPath);
                        if (subs.Any(s => IsProductFolder(_fileSystem.GetFileName(s))))
                        {
                            isExplicitOrder = true;
                        }
                    }
                    catch { throw; }
                }
            }

            if (!isExplicitOrder && IsProductFolder(parts[2]))
            {
                string specName = parts[2];
                string orderRelPath = _fileSystem.Combine(date, customerName);
                string orderFullPath = _fileSystem.Combine(rootFolder, orderRelPath);
                string specRelPath = _fileSystem.Combine(orderRelPath, specName);
                string specFullPath = _fileSystem.Combine(rootFolder, specRelPath);

                return new[]
                {
                    new DiscoveredOrder(
                        Date: date,
                        OriginalCustomerFolderName: customerName,
                        FullPath: orderFullPath,
                        RelativePath: orderRelPath,
                        Specifications: new[]
                        {
                            new DiscoveredSpecification(specName, specFullPath, specRelPath)
                        },
                        Kind: OrderKind.Implicit,
                        OrderName: customerName
                    )
                };
            }
            else
            {
                // parts[2] is an explicit order (e.g. Minh Studio\Don 01)
                string explicitOrderName = parts[2];
                string orderRelPath = _fileSystem.Combine(date, customerName, explicitOrderName);
                string orderFullPath = _fileSystem.Combine(rootFolder, orderRelPath);

                if (parts.Length == 3)
                {
                    var specs = DiscoverSpecificationsInFolder(rootFolder, orderFullPath);
                    return new[]
                    {
                        new DiscoveredOrder(
                            Date: date,
                            OriginalCustomerFolderName: customerName,
                            FullPath: orderFullPath,
                            RelativePath: orderRelPath,
                            Specifications: specs,
                            Kind: OrderKind.Explicit,
                            OrderName: explicitOrderName
                        )
                    };
                }
                else
                {
                    string specName = parts[3];
                    string specRelPath = _fileSystem.Combine(orderRelPath, specName);
                    string specFullPath = _fileSystem.Combine(rootFolder, specRelPath);
                    return new[]
                    {
                        new DiscoveredOrder(
                            Date: date,
                            OriginalCustomerFolderName: customerName,
                            FullPath: orderFullPath,
                            RelativePath: orderRelPath,
                            Specifications: new[] { new DiscoveredSpecification(specName, specFullPath, specRelPath) },
                            Kind: OrderKind.Explicit,
                            OrderName: explicitOrderName
                        )
                    };
                }
            }
        }
        else if (parts.Length >= 2 && string.Equals(parts[0], "KHACH_LE", StringComparison.OrdinalIgnoreCase))
        {
            string guestName = parts[1];
            string guestRelPath = _fileSystem.Combine("KHACH_LE", guestName);
            string guestFullPath = _fileSystem.Combine(rootFolder, guestRelPath);
            dateString ??= DateTime.UtcNow.ToString("yyyy-MM-dd");

            if (parts.Length == 2)
            {
                var specs = DiscoverSpecificationsInFolder(rootFolder, guestFullPath);
                return new[]
                {
                    new DiscoveredOrder(
                        Date: dateString,
                        OriginalCustomerFolderName: guestName,
                        FullPath: guestFullPath,
                        RelativePath: guestRelPath,
                        Specifications: specs,
                        Kind: OrderKind.Implicit,
                        OrderName: guestName
                    )
                };
            }
            else
            {
                string specName = parts[2];
                string specRelPath = _fileSystem.Combine(guestRelPath, specName);
                string specFullPath = _fileSystem.Combine(rootFolder, specRelPath);
                return new[]
                {
                    new DiscoveredOrder(
                        Date: dateString,
                        OriginalCustomerFolderName: guestName,
                        FullPath: guestFullPath,
                        RelativePath: guestRelPath,
                        Specifications: new[] { new DiscoveredSpecification(specName, specFullPath, specRelPath) },
                        Kind: OrderKind.Implicit,
                        OrderName: guestName
                    )
                };
            }
        }

        var orders = new List<DiscoveredOrder>();
        var directChildDirs = _fileSystem.EnumerateDirectories(folderPath).ToList();

        var directProductDirs = new List<string>();
        var explicitOrderDirs = new List<string>();
        var unresolvedCandidates = new List<string>();

        foreach (var childDir in directChildDirs)
        {
            string childName = _fileSystem.GetFileName(childDir);
            var grandChildren = _fileSystem.EnumerateDirectories(childDir).ToList();

            // Check if child itself has subfolders that resolve to products
            bool hasProductSubfolders = grandChildren.Any(gc => IsProductFolder(_fileSystem.GetFileName(gc)));

            if (hasProductSubfolders)
            {
                // Case B / C: child is an Explicit Order
                explicitOrderDirs.Add(childDir);
            }
            else if (IsProductFolder(childName))
            {
                // Case A / C: child is a direct Product folder
                directProductDirs.Add(childDir);
            }
            else
            {
                // Neither known product nor has known product subfolders
                // If it has subfolders, treat as explicit order candidate; else direct spec candidate
                if (grandChildren.Count > 0)
                {
                    explicitOrderDirs.Add(childDir);
                }
                else
                {
                    unresolvedCandidates.Add(childDir);
                }
            }
        }

        // If we found explicit order directories, create an explicit order for each
        foreach (var orderDir in explicitOrderDirs)
        {
            string orderName = _fileSystem.GetFileName(orderDir);
            string relativeOrderPath = _fileSystem.GetRelativePath(rootFolder, orderDir);
            var specs = DiscoverSpecificationsInFolder(rootFolder, orderDir);

            orders.Add(new DiscoveredOrder(
                Date: dateString,
                OriginalCustomerFolderName: customerFolderName,
                FullPath: orderDir,
                RelativePath: relativeOrderPath,
                Specifications: specs,
                Kind: OrderKind.Explicit,
                OrderName: orderName
            ));
        }

        // For direct products or unresolved direct candidates: attach to Implicit Order
        var implicitSpecsDirs = directProductDirs.Concat(unresolvedCandidates).ToList();
        if (implicitSpecsDirs.Count > 0 || (explicitOrderDirs.Count == 0 && directChildDirs.Count == 0))
        {
            var specs = implicitSpecsDirs
                .Select(d => new DiscoveredSpecification(
                    FolderName: _fileSystem.GetFileName(d),
                    FullPath: d,
                    RelativePath: _fileSystem.GetRelativePath(rootFolder, d)))
                .OrderBy(s => s.FolderName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            orders.Add(new DiscoveredOrder(
                Date: dateString,
                OriginalCustomerFolderName: customerFolderName,
                FullPath: folderPath,
                RelativePath: relativeCustomerPath,
                Specifications: specs,
                Kind: OrderKind.Implicit,
                OrderName: customerFolderName
            ));
        }

        return orders;
    }

    public DiscoveredOrder? DiscoverSingleOrder(string rootFolder, string orderRelativePath)
    {
        string fullPath = _fileSystem.Combine(rootFolder, orderRelativePath);
        if (!_fileSystem.DirectoryExists(fullPath))
        {
            return null;
        }

        string normalized = orderRelativePath.Replace('/', '\\').Trim('\\');
        string[] parts = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries);

        string dateString;
        string customerFolderName;
        OrderKind kind;
        string orderName;
        string resolvedOrderRelativePath = orderRelativePath;
        string resolvedOrderFullPath = fullPath;
        IReadOnlyList<DiscoveredSpecification> specs;

        if (parts.Length >= 3 && DatePatternRegex.IsMatch(parts[0]))
        {
            dateString = parts[0];
            string secondPart = parts[1];

            // Subcase: KHACH_LE
            if (string.Equals(secondPart, "KHACH_LE", StringComparison.OrdinalIgnoreCase))
            {
                customerFolderName = parts[2];
                orderName = parts[2];
                kind = OrderKind.Explicit;
                resolvedOrderRelativePath = _fileSystem.Combine(parts[0], parts[1], parts[2]);
                resolvedOrderFullPath = _fileSystem.Combine(rootFolder, resolvedOrderRelativePath);

                if (parts.Length == 3)
                {
                    specs = DiscoverSpecificationsInFolder(rootFolder, resolvedOrderFullPath);
                }
                else
                {
                    string specName = parts[3];
                    string specRelPath = _fileSystem.Combine(resolvedOrderRelativePath, specName);
                    string specFullPath = _fileSystem.Combine(rootFolder, specRelPath);
                    specs = new[] { new DiscoveredSpecification(specName, specFullPath, specRelPath) };
                }
            }
            bool isExplicitOrder = (parts.Length >= 4 && IsProductFolder(parts[3]));
            if (!isExplicitOrder)
            {
                string potentialOrderPath = _fileSystem.Combine(rootFolder, parts[0], parts[1], parts[2]);
                if (_fileSystem.DirectoryExists(potentialOrderPath))
                {
                    try
                    {
                        var subs = _fileSystem.EnumerateDirectories(potentialOrderPath);
                        if (subs.Any(s => IsProductFolder(_fileSystem.GetFileName(s))))
                        {
                            isExplicitOrder = true;
                        }
                    }
                    catch { throw; }
                }
            }

            if (!isExplicitOrder && IsProductFolder(parts[2]))
            {
                // parts[2] is a product spec folder e.g. 2026-09-29\Quang Studio\40x60 TG
                customerFolderName = secondPart;
                orderName = customerFolderName;
                kind = OrderKind.Implicit;
                resolvedOrderRelativePath = _fileSystem.Combine(parts[0], parts[1]);
                resolvedOrderFullPath = _fileSystem.Combine(rootFolder, resolvedOrderRelativePath);

                string specName = parts[2];
                string specRelPath = _fileSystem.Combine(resolvedOrderRelativePath, specName);
                string specFullPath = _fileSystem.Combine(rootFolder, specRelPath);
                specs = new[] { new DiscoveredSpecification(specName, specFullPath, specRelPath) };
            }
            else
            {
                // Explicit order e.g. 2026-09-28\Anh An\Don 01
                customerFolderName = secondPart;
                orderName = parts[2];
                kind = OrderKind.Explicit;
                resolvedOrderRelativePath = _fileSystem.Combine(parts[0], parts[1], parts[2]);
                resolvedOrderFullPath = _fileSystem.Combine(rootFolder, resolvedOrderRelativePath);

                if (parts.Length == 3)
                {
                    specs = DiscoverSpecificationsInFolder(rootFolder, resolvedOrderFullPath);
                }
                else
                {
                    string specName = parts[3];
                    string specRelPath = _fileSystem.Combine(resolvedOrderRelativePath, specName);
                    string specFullPath = _fileSystem.Combine(rootFolder, specRelPath);
                    specs = new[] { new DiscoveredSpecification(specName, specFullPath, specRelPath) };
                }
            }
        }
        else if (parts.Length == 2 && DatePatternRegex.IsMatch(parts[0]))
        {
            // Implicit order e.g. 2026-09-28\Anh An
            dateString = parts[0];
            customerFolderName = parts[1];
            orderName = customerFolderName;
            kind = OrderKind.Implicit;
            resolvedOrderRelativePath = _fileSystem.Combine(parts[0], parts[1]);
            resolvedOrderFullPath = _fileSystem.Combine(rootFolder, resolvedOrderRelativePath);
            specs = DiscoverSpecificationsInFolder(rootFolder, resolvedOrderFullPath);
        }
        else
        {
            dateString = string.Empty;
            customerFolderName = _fileSystem.GetFileName(fullPath);
            orderName = customerFolderName;
            kind = OrderKind.Implicit;
            resolvedOrderRelativePath = orderRelativePath;
            resolvedOrderFullPath = fullPath;
            specs = DiscoverSpecificationsInFolder(rootFolder, fullPath);
        }

        return new DiscoveredOrder(
            Date: dateString,
            OriginalCustomerFolderName: customerFolderName,
            FullPath: resolvedOrderFullPath,
            RelativePath: resolvedOrderRelativePath,
            Specifications: specs,
            Kind: kind,
            OrderName: orderName
        );
    }

    public DiscoveredSpecification? DiscoverSingleSpecification(string rootFolder, string specRelativePath)
    {
        string fullPath = _fileSystem.Combine(rootFolder, specRelativePath);
        if (!_fileSystem.DirectoryExists(fullPath))
        {
            return null;
        }

        string specFolderName = _fileSystem.GetFileName(fullPath);

        return new DiscoveredSpecification(
            FolderName: specFolderName,
            FullPath: fullPath,
            RelativePath: specRelativePath
        );
    }

    private List<DiscoveredSpecification> DiscoverSpecificationsInFolder(string rootFolder, string folderPath)
    {
        var specs = new List<DiscoveredSpecification>();
        try
        {
            foreach (var specDir in _fileSystem.EnumerateDirectories(folderPath))
            {
                string specFolderName = _fileSystem.GetFileName(specDir);
                string relativeSpecPath = _fileSystem.GetRelativePath(rootFolder, specDir);

                specs.Add(new DiscoveredSpecification(
                    FolderName: specFolderName,
                    FullPath: specDir,
                    RelativePath: relativeSpecPath
                ));
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to enumerate specifications in '{Folder}'", folderPath);
            throw;
        }

        return specs.OrderBy(s => s.FolderName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private bool IsProductFolder(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName)) return false;

        if (_isProductPredicate != null)
        {
            return _isProductPredicate(folderName);
        }

        if (_productResolver != null)
        {
            try
            {
                var res = _productResolver.ResolveProductAsync(folderName).GetAwaiter().GetResult();
                if (res.Status == PrintSpecificationResolutionStatus.Resolved ||
                    res.Status == PrintSpecificationResolutionStatus.AmbiguousCollision)
                {
                    return true;
                }
            }
            catch
            {
                // Fallback
            }
        }

        if (_specRepository != null)
        {
            try
            {
                var specs = _specRepository.GetAllAsync(includeInactive: false).GetAwaiter().GetResult();
                string normalized = CustomerNormalizer.Normalize(folderName);
                if (specs.Any(s =>
                    string.Equals(CustomerNormalizer.Normalize(s.CanonicalName), normalized, StringComparison.OrdinalIgnoreCase) ||
                    s.Aliases.Any(a => string.Equals(a.NormalizedAlias, normalized, StringComparison.OrdinalIgnoreCase))))
                {
                    return true;
                }
            }
            catch
            {
                // Fallback to heuristic
            }
        }

        return HeuristicIsProductFolder(folderName);
    }

    public static bool HeuristicIsProductFolder(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName)) return false;

        string norm = CustomerNormalizer.Normalize(folderName);
        string unaccented = CustomerNormalizer.RemoveDiacritics(norm).ToLowerInvariant();

        // Explicit order prefixes must NEVER be treated as product folders
        if (unaccented.StartsWith("don ") ||
            unaccented.StartsWith("don-") ||
            unaccented.StartsWith("don_") ||
            unaccented.StartsWith("order ") ||
            unaccented.StartsWith("order-") ||
            unaccented.StartsWith("order_") ||
            unaccented.StartsWith("hop ") ||
            unaccented.StartsWith("set ") ||
            unaccented.StartsWith("goi "))
        {
            return false;
        }

        // Dimension pair extraction
        if (SizeNormalizer.TryExtractDimensions(folderName, out _, out _, out _))
            return true;

        if (DimensionRegex.IsMatch(norm)) return true;

        if (norm.StartsWith("album", StringComparison.OrdinalIgnoreCase) ||
            norm.StartsWith("alb", StringComparison.OrdinalIgnoreCase) ||
            norm.StartsWith("in ", StringComparison.OrdinalIgnoreCase) ||
            norm.StartsWith("anh ", StringComparison.OrdinalIgnoreCase) ||
            norm.StartsWith("khung", StringComparison.OrdinalIgnoreCase) ||
            norm.StartsWith("canvas", StringComparison.OrdinalIgnoreCase) ||
            norm.StartsWith("photobook", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}

