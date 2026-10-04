using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LalabAutoReport.Core.Domain;

namespace LalabAutoReport.Core.Interfaces;

public class OrderThumbnailInfo
{
    public long OrderId { get; set; }
    public string Status { get; set; } = "NONE"; // NONE, PENDING, READY
    public string? Key { get; set; }
    public string? Version { get; set; }
    public string? LocalCachePath { get; set; }
    public bool Uploaded { get; set; }
}

public interface ICloudMediaSyncService
{
    Task<OrderThumbnailInfo> EnsureOrderThumbnailUploadedAsync(
        Order order,
        string apiUrl,
        string secret,
        CancellationToken cancellationToken = default);

    Task<Dictionary<long, OrderThumbnailInfo>> EnsureThumbnailsUploadedForOrdersAsync(
        IEnumerable<Order> orders,
        string apiUrl,
        string secret,
        CancellationToken cancellationToken = default);
}
