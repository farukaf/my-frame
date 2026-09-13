using System.Security.Cryptography;
using System.Text;
using System.Runtime.Versioning;

namespace MyFrame.Core;

/// <summary>
/// Windows-user protected credential store for Warframe.Market. Existing plaintext files are read
/// for compatibility, but every token saved through this store is DPAPI-protected and never enters
/// SQLite, logs or MCP responses.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ProtectedFileMarketTokenStore : IMarketTokenStore
{
    private static readonly byte[] Header = Encoding.ASCII.GetBytes("MYFRAME-DPAPI-V1\0");
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ProtectedFileMarketTokenStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Token path is required.", nameof(path));
        _path = Path.GetFullPath(path);
    }

    public async Task<string?> ReadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path)) return null;
            var bytes = await File.ReadAllBytesAsync(_path, cancellationToken).ConfigureAwait(false);
            if (bytes.AsSpan().StartsWith(Header))
            {
                try
                {
                    var plaintext = ProtectedData.Unprotect(bytes.AsSpan(Header.Length).ToArray(), null,
                        DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(plaintext).Trim();
                }
                catch (CryptographicException) { return null; }
            }
            // Compatibility only: do not rewrite legacy plaintext implicitly.
            return Encoding.UTF8.GetString(bytes).Trim();
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(string token, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(token.Trim()), null,
                DataProtectionScope.CurrentUser);
            var payload = new byte[Header.Length + protectedBytes.Length];
            Header.CopyTo(payload, 0);
            protectedBytes.CopyTo(payload, Header.Length);
            var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, payload, cancellationToken).ConfigureAwait(false);
                File.Move(temporary, _path, true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
        finally { _gate.Release(); }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(_path)) File.Delete(_path);
        }
        finally { _gate.Release(); }
    }
}
