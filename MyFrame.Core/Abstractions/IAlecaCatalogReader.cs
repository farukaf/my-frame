namespace MyFrame.Core;

public interface IAlecaCatalogReader
{
    Task<CatalogSnapshot> LoadAsync(string alecaDirectory, CancellationToken cancellationToken = default);
}
