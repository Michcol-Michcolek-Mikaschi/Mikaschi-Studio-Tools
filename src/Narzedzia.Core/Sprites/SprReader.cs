using System.Buffers.Binary;

namespace Narzedzia.Core.Sprites;

/// <summary>
/// Czytnik klasycznego pliku .spr [archiwum sprite'ow Tibii].
/// </summary>
public sealed class SprReader : IDisposable
{
    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private uint[] _offsets = Array.Empty<uint>();

    public SprReader(string path, bool transparent = false, bool extended = true)
        : this(File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read), transparent, extended, ownsStream: true)
    {
    }

    public SprReader(Stream stream, bool transparent = false, bool extended = true, bool ownsStream = false)
    {
        _stream = stream;
        _ownsStream = ownsStream;
        Transparent = transparent;

        ReadHeader(extended);
    }

    public uint Signature { get; private set; }

    public uint SpriteCount => (uint)_offsets.Length;

    public bool Transparent { get; }

    public Sprite Read(uint spriteId)
    {
        if (spriteId == 0 || spriteId > _offsets.Length)
        {
            return Sprite.Empty(spriteId);
        }

        var offset = _offsets[spriteId - 1];
        if (offset == 0)
        {
            return Sprite.Empty(spriteId);
        }

        _stream.Position = offset + 3;

        Span<byte> sizeBuffer = stackalloc byte[2];
        ReadExact(sizeBuffer);
        var size = BinaryPrimitives.ReadUInt16LittleEndian(sizeBuffer);

        var sprite = new Sprite
        {
            Id = spriteId,
            Size = size,
            Transparent = Transparent,
            CompressedPixels = new byte[size]
        };

        ReadExact(sprite.CompressedPixels);
        return sprite;
    }

    public void Dispose()
    {
        if (_ownsStream)
        {
            _stream.Dispose();
        }
    }

    private void ReadHeader(bool extended)
    {
        Span<byte> buffer4 = stackalloc byte[4];
        ReadExact(buffer4);
        Signature = BinaryPrimitives.ReadUInt32LittleEndian(buffer4);

        uint count;
        if (extended)
        {
            ReadExact(buffer4);
            count = BinaryPrimitives.ReadUInt32LittleEndian(buffer4);
        }
        else
        {
            Span<byte> buffer2 = stackalloc byte[2];
            ReadExact(buffer2);
            count = BinaryPrimitives.ReadUInt16LittleEndian(buffer2);
        }

        _offsets = new uint[count];
        for (var i = 0; i < count; i++)
        {
            ReadExact(buffer4);
            _offsets[i] = BinaryPrimitives.ReadUInt32LittleEndian(buffer4);
        }
    }

    private void ReadExact(Span<byte> buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var current = _stream.Read(buffer[read..]);
            if (current <= 0)
            {
                throw new EndOfStreamException("Nieoczekiwany koniec pliku .spr.");
            }

            read += current;
        }
    }
}
