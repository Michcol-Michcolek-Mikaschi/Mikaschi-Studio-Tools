using SharpCompress.Compressors.LZMA;

namespace Narzedzia.Core.Compression;

/// <summary>
/// Koduje i dekoduje standardowy kontener LZMA-Alone: 5 bajtów właściwości,
/// 8 bajtów rozmiaru po dekompresji i właściwy strumień LZMA.
/// </summary>
public static class LzmaDataCodec
{
    private const int DefaultDictionarySize = 1 << 23;

    public static byte[] Compress(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        using var body = new MemoryStream();
        byte[] properties;
        using (var lzma = LzmaStream.Create(
                   new LzmaEncoderProperties(true, DefaultDictionarySize),
                   false,
                   body))
        {
            lzma.Write(data, 0, data.Length);
            lzma.Flush();
            properties = lzma.Properties;
        }

        using var output = new MemoryStream(properties.Length + sizeof(long) + (int)body.Length);
        output.Write(properties);
        output.Write(BitConverter.GetBytes((long)data.Length));
        body.Position = 0;
        body.CopyTo(output);
        return output.ToArray();
    }

    public static byte[] Decompress(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < 13)
        {
            throw new InvalidDataException("Dane są za krótkie, aby zawierać nagłówek LZMA.");
        }

        var properties = data[..5];
        var outputSize = BitConverter.ToInt64(data, 5);
        if (outputSize < -1)
        {
            throw new InvalidDataException("Nieprawidłowy rozmiar danych zapisany w nagłówku LZMA.");
        }

        using var input = new MemoryStream(data, 13, data.Length - 13, writable: false);
        using var lzma = LzmaStream.Create(properties, input, -1, outputSize, leaveOpen: false);
        using var output = outputSize is >= 0 and <= int.MaxValue
            ? new MemoryStream((int)outputSize)
            : new MemoryStream();
        lzma.CopyTo(output);

        if (outputSize >= 0 && output.Length != outputSize)
        {
            throw new InvalidDataException(
                $"Rozmiar danych po dekompresji ({output.Length}) nie zgadza się z nagłówkiem ({outputSize}).");
        }

        return output.ToArray();
    }
}
