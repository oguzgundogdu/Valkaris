using System.Globalization;

namespace Valkaris.Core.Network;

/// <summary>What a person types to reach a host, and which of this machine's addresses are worth sharing.</summary>
public static class NetAddress
{
    /// <summary>An address, with or without ":port". Null if it can't be one.</summary>
    public static (string Host, int Port)? Parse(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed.Any(char.IsWhiteSpace))
            return null;

        var colon = trimmed.LastIndexOf(':');
        if (colon < 0)
            return (trimmed, Handshake.DefaultPort);
        if (colon == 0 || trimmed.IndexOf(':') != colon)
            return null;

        return int.TryParse(trimmed[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is > 0 and <= 65535
            ? (trimmed[..colon], port)
            : null;
    }

    /// <summary>IPv4 addresses others could reach, including Tailscale's. Leaves out this machine's own loopback and self-assigned ones.</summary>
    public static IReadOnlyList<string> Shareable(IEnumerable<string> addresses) =>
        addresses.Where(a => a.Count(c => c == '.') == 3 && !a.Contains(':') && !a.StartsWith("127.", StringComparison.Ordinal) && !a.StartsWith("169.254.", StringComparison.Ordinal))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();
}
