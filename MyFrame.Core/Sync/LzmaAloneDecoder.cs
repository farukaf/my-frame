using System.Text;
using SharpCompress.Compressors.LZMA;

namespace MyFrame.Core.Sync;

/// <summary>Decoder for the LZMA-Alone envelope used by the DE Public Export.</summary>
public sealed class LzmaAloneDecoder(int maximumOutputBytes = 64 * 1024 * 1024)
{
    public string Decode(byte[] compressed)
    {
        ArgumentNullException.ThrowIfNull(compressed);
        if (compressed.Length < 13) throw new InvalidDataException("PUBLIC_EXPORT_LZMA_HEADER_INVALID");
        var outputSize = BitConverter.ToInt64(compressed, 5);
        if (outputSize < -1 || outputSize > maximumOutputBytes) throw new InvalidDataException("PUBLIC_EXPORT_LZMA_OUTPUT_TOO_LARGE");
        using var input = new MemoryStream(compressed, 13, compressed.Length - 13, writable: false);
        using var decoder = LzmaStream.Create(compressed[..5], input, input.Length, outputSize, leaveOpen: false);
        using var output = new MemoryStream(outputSize is > 0 ? (int)outputSize : Math.Min(maximumOutputBytes, 1024 * 1024));
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = decoder.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length > maximumOutputBytes - read) throw new InvalidDataException("PUBLIC_EXPORT_LZMA_OUTPUT_TOO_LARGE");
            output.Write(buffer, 0, read);
        }
        if (output.Length > maximumOutputBytes || (outputSize >= 0 && output.Length != outputSize)) throw new InvalidDataException("PUBLIC_EXPORT_LZMA_SIZE_MISMATCH");
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(output.GetBuffer(), 0, checked((int)output.Length));
    }
}
