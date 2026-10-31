using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Valkaris.Core.Data;

namespace Valkaris.Core.Network;

/// <summary>What must match before two machines can play: the same game and the same balance data.</summary>
public static class Handshake
{
    /// <summary>Change this whenever the wire format changes.</summary>
    public const string Version = "0.7.0";

    public const int DefaultPort = 24565;
    public const string AnyAddress = "*";

    /// <summary>A port as people type it, with no grouping.</summary>
    public static string PortText(int port) => port.ToString(CultureInfo.InvariantCulture);

    public static string DataHash(IDataSource source)
    {
        using var sha = SHA256.Create();
        foreach (var file in GameData.Files)
        {
            // Git may check text out with other line endings on another system.
            var bytes = Encoding.UTF8.GetBytes(file + "\n" + source.ReadText(file).Replace("\r\n", "\n"));
            sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!);
    }
}
