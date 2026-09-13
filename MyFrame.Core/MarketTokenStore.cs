using System.Text;

namespace MyFrame.Core;

public interface IMarketTokenStore
{
    Task<string?> ReadAsync(CancellationToken cancellationToken = default);
}

/// <summary>Local compatibility store for the market credential. UI can replace this with a credential-vault implementation.</summary>
public sealed class FileMarketTokenStore(string path) : IMarketTokenStore
{
    private readonly string _path = Path.GetFullPath(path ?? throw new ArgumentNullException(nameof(path)));

    public async Task<string?> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return null;
        await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: false);
        return (await reader.ReadToEndAsync(cancellationToken)).Trim();
    }
}
