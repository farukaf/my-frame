using System.Security.Cryptography;
using System.Text;

namespace MyFrame.Core;

public static class MyFrameContext
{
    public static string ComputeId(string directory)
    {
        var normalized = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar).ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..16];
    }
}
