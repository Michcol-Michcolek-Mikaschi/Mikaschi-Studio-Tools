using Avalonia.Media.Imaging;
using Narzedzia.Core.Models;

namespace Modules.ObjectBuilder.Services;

/// <summary>
/// Ograniczony cache LRU: przechowuje wyłącznie ostatnio widziane miniatury,
/// dzięki czemu listy z dziesiątkami tysięcy sprite'ów nie zjadają pamięci.
/// </summary>
public sealed class LegacyThumbnailCache : IDisposable
{
    private const int Capacity = 600;
    private readonly LegacySpriteStore _sprites;
    private readonly Dictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _usage = new();

    public LegacyThumbnailCache(LegacySpriteStore sprites)
    {
        _sprites = sprites;
    }

    public Bitmap? GetThing(DatThingType thing)
    {
        var key = $"thing:{(int)thing.Category}:{thing.Id}";
        return GetOrCreate(key, () =>
        {
            var rendered = LegacyThingRenderer.Render(thing, _sprites, 0, 0, 0, 0, 0);
            return rendered is null
                ? null
                : LegacyBitmapFactory.FromBgra(rendered.Pixels, rendered.Width, rendered.Height);
        });
    }

    public Bitmap? GetSprite(uint spriteId)
    {
        if (spriteId == 0)
        {
            return null;
        }

        return GetOrCreate($"sprite:{spriteId}", () =>
        {
            var pixels = _sprites.GetSpritePixels(spriteId);
            return pixels is null ? null : LegacyBitmapFactory.FromBgra(pixels, 32, 32);
        });
    }

    public void InvalidateThing(DatThingType thing)
    {
        Remove($"thing:{(int)thing.Category}:{thing.Id}");
    }

    public void Clear()
    {
        foreach (var entry in _entries.Values)
        {
            entry.Bitmap.Dispose();
        }

        _entries.Clear();
        _usage.Clear();
    }

    public void Dispose() => Clear();

    private Bitmap? GetOrCreate(string key, Func<Bitmap?> factory)
    {
        if (_entries.TryGetValue(key, out var existing))
        {
            Touch(existing);
            return existing.Bitmap;
        }

        var bitmap = factory();
        if (bitmap is null)
        {
            return null;
        }

        var node = _usage.AddFirst(key);
        _entries[key] = new CacheEntry(bitmap, node);
        Trim();
        return bitmap;
    }

    private void Touch(CacheEntry entry)
    {
        _usage.Remove(entry.Node);
        _usage.AddFirst(entry.Node);
    }

    private void Trim()
    {
        while (_entries.Count > Capacity && _usage.Last is { } last)
        {
            Remove(last.Value);
        }
    }

    private void Remove(string key)
    {
        if (!_entries.Remove(key, out var entry))
        {
            return;
        }

        _usage.Remove(entry.Node);
        entry.Bitmap.Dispose();
    }

    private sealed record CacheEntry(Bitmap Bitmap, LinkedListNode<string> Node);
}
