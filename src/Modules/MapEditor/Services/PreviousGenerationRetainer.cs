namespace Modules.MapEditor.Services;

/// <summary>
/// Keeps one replaced disposable generation alive. UI controls can retain image
/// references for a render pass after their bound collection has been replaced;
/// disposing that generation immediately would invalidate those images mid-frame.
/// A second replacement proves the first retired generation is no longer current
/// in the UI, while keeping memory usage bounded to one retired generation.
/// </summary>
internal sealed class PreviousGenerationRetainer<T> : IDisposable
    where T : class, IDisposable
{
    private readonly object _sync = new();
    private T? _previous;
    private bool _disposed;

    internal bool HasRetainedGeneration
    {
        get
        {
            lock (_sync) return _previous is not null;
        }
    }

    public void Retain(T generation)
    {
        ArgumentNullException.ThrowIfNull(generation);
        T? staleGeneration;
        lock (_sync)
        {
            if (_disposed)
            {
                staleGeneration = generation;
            }
            else
            {
                if (ReferenceEquals(_previous, generation)) return;
                staleGeneration = _previous;
                _previous = generation;
            }
        }

        staleGeneration?.Dispose();
    }

    public void Dispose()
    {
        T? retainedGeneration;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            retainedGeneration = _previous;
            _previous = null;
        }

        retainedGeneration?.Dispose();
    }
}
