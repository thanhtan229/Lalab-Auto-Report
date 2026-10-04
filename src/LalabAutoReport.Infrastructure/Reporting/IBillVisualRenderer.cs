using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using LalabAutoReport.Core.Domain;

namespace LalabAutoReport.Infrastructure.Reporting;

/// <summary>
/// Service for rendering customer bill pages directly to in-memory WPF BitmapSource images
/// and saving on demand, without forcing disk writes or third-party image viewers.
/// </summary>
public interface IBillVisualRenderer
{
    /// <summary>
    /// Renders all paginated pages of a customer bill into in-memory BitmapSource objects.
    /// </summary>
    Task<IReadOnlyList<BitmapSource>> RenderBillBitmapsAsync(CustomerBill bill, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a rendered bitmap to a specified JPEG file on disk on demand.
    /// </summary>
    Task<string> SaveBitmapToJpegAsync(BitmapSource bitmap, string destinationFilePath, int quality = 95, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stitches multiple bill page bitmaps vertically into a single seamless continuous bitmap.
    /// </summary>
    BitmapSource StitchBitmapsVertically(IReadOnlyList<BitmapSource> pages);
}
