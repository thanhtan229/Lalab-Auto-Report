using System;

namespace LalabAutoReport.Core.Services;

public static class MobileAccessLink
{
    public static string Create(string baseUrl, string lanToken, bool useCloudLogin)
        => useCloudLogin ? baseUrl.TrimEnd('/') + "/"
            : baseUrl.TrimEnd('/') + "/?auth=" + Uri.EscapeDataString(lanToken);
}
