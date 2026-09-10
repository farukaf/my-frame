namespace MyFrame.Core;

public interface IAlecaFrameReader
{
    Task<InventorySnapshot> ReadAsync(string alecaDirectory, CancellationToken cancellationToken = default);
}
