using System.Security.Cryptography;
using System.Text;

namespace KenoKoch.Web.Hosting;

/// <summary>
/// Anonymer Schlüssel pro Besucher und Tag für das Rate Limiting des Gästebuchs.
/// Die IP-Adresse wird nur gehasht (HMAC mit zufälligem Schlüssel pro Prozess) und nirgends gespeichert.
/// </summary>
public static class VisitorKey
{
    private static readonly byte[] Secret = RandomNumberGenerator.GetBytes(32);

    public static string From(HttpContext? context)
    {
        var address = context?.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var day = DateTime.UtcNow.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        var hash = HMACSHA256.HashData(Secret, Encoding.UTF8.GetBytes($"{address}|{day}"));
        return Convert.ToHexString(hash, 0, 12);
    }
}
