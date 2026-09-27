using SharpCompress.Compressors.LZMA;

namespace Narzedzia.Core.Assets;

public static class LzmaImageCodec
{
    private static readonly byte[] CipSoftMarker = [0x70, 0x0A, 0xFA, 0x80, 0x24];

    public static byte[] ReadImageBytes(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return DecodeImageBytes(bytes);
    }

    public static byte[] DecodeImageBytes(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (TryGetCipSoftPayload(bytes, out var cipPayload))
        {
            return TryDecode(cipPayload.Properties, bytes, cipPayload.PayloadOffset, -1) ?? bytes;
        }

        if (TryGetRawPayload(bytes, out var rawPayload))
        {
            return TryDecode(rawPayload.Properties, bytes, rawPayload.PayloadOffset, rawPayload.OutputSize) ?? bytes;
        }

        return bytes;
    }

    public static void WriteCipSoftImage(string path, byte[] imageBytes)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, EncodeCipSoftImage(imageBytes));
    }

    public static byte[] EncodeCipSoftImage(byte[] imageBytes)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);

        var (properties, body) = EncodeLzma(imageBytes);
        using var payload = new MemoryStream();
        payload.Write(properties);
        payload.Write(BitConverter.GetBytes(-1L));
        payload.Write(body);

        var payloadBytes = payload.ToArray();
        var marker = BuildCipSoftMarker(payloadBytes.Length);
        var prefix = new byte[32];
        var markerOffset = Math.Max(0, prefix.Length - marker.Length);
        marker.CopyTo(prefix.AsSpan(markerOffset));

        using var output = new MemoryStream(prefix.Length + payloadBytes.Length);
        output.Write(prefix);
        output.Write(payloadBytes);
        return output.ToArray();
    }

    /// <summary>
    /// CipSoft Tibia używa LZMA z dictionarySize=32MB (1&lt;&lt;25) — zgodnie z oryginalnym
    /// WPF Assets-Editor LZMA.cs (linia ~74). Mniejsze słowniki produkują arkusze
    /// odczytywalne przez nasz decoder, ale niezgodne z klientami CipSoft 12+/13+.
    /// </summary>
    private const int CipSoftDictionarySize = 1 << 25;

    private static (byte[] Properties, byte[] Body) EncodeLzma(byte[] bytes)
    {
        using var body = new MemoryStream();
        byte[] properties;
        using (var lzma = LzmaStream.Create(new LzmaEncoderProperties(true, CipSoftDictionarySize), false, body))
        {
            lzma.Write(bytes, 0, bytes.Length);
            lzma.Flush();
            properties = lzma.Properties;
        }

        return (properties, body.ToArray());
    }

    private static byte[]? TryDecode(byte[] properties, byte[] source, int payloadOffset, long outputSize)
    {
        try
        {
            using var input = new MemoryStream(source, payloadOffset, source.Length - payloadOffset);
            using var lzma = LzmaStream.Create(properties, input, -1, outputSize, leaveOpen: false);
            using var output = new MemoryStream();
            lzma.CopyTo(output);
            return output.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static bool TryGetRawPayload(byte[] bytes, out PayloadInfo payload)
    {
        payload = default;
        if (bytes.Length < 13)
        {
            return false;
        }

        var properties = bytes[..5];
        var outputSize = BitConverter.ToInt64(bytes, 5);
        if (outputSize < -1)
        {
            return false;
        }

        payload = new PayloadInfo(properties, 13, outputSize);
        return true;
    }

    private static bool TryGetCipSoftPayload(byte[] bytes, out PayloadInfo payload)
    {
        payload = default;
        var markerOffset = FindCipSoftMarker(bytes);
        if (markerOffset < 0)
        {
            return false;
        }

        var offset = markerOffset + CipSoftMarker.Length;
        if (!TryRead7BitEncodedInt(bytes, ref offset, out _))
        {
            return false;
        }

        if (offset + 13 > bytes.Length)
        {
            return false;
        }

        var properties = bytes[offset..(offset + 5)];
        payload = new PayloadInfo(properties, offset + 13, -1);
        return true;
    }

    private static int FindCipSoftMarker(byte[] bytes)
    {
        var maxStart = Math.Min(40, bytes.Length - CipSoftMarker.Length);
        for (var i = 0; i <= maxStart; i++)
        {
            var found = true;
            for (var j = 0; j < CipSoftMarker.Length; j++)
            {
                if (bytes[i + j] != CipSoftMarker[j])
                {
                    found = false;
                    break;
                }
            }

            if (found)
            {
                return i;
            }
        }

        return -1;
    }

    private static byte[] BuildCipSoftMarker(int payloadSize)
    {
        using var output = new MemoryStream();
        output.Write(CipSoftMarker);
        Write7BitEncodedInt(output, payloadSize);
        return output.ToArray();
    }

    private static bool TryRead7BitEncodedInt(byte[] bytes, ref int offset, out int value)
    {
        value = 0;
        var shift = 0;
        while (shift < 35 && offset < bytes.Length)
        {
            var current = bytes[offset++];
            value |= (current & 0x7F) << shift;
            if ((current & 0x80) == 0)
            {
                return true;
            }

            shift += 7;
        }

        return false;
    }

    private static void Write7BitEncodedInt(Stream stream, int value)
    {
        var remaining = (uint)value;
        while (remaining >= 0x80)
        {
            stream.WriteByte((byte)(remaining | 0x80));
            remaining >>= 7;
        }

        stream.WriteByte((byte)remaining);
    }

    private readonly record struct PayloadInfo(byte[] Properties, int PayloadOffset, long OutputSize);
}
