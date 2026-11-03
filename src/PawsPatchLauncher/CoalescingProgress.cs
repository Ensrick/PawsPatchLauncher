namespace PawsPatchLauncher;

/// <summary>Keep the latest progress sample and at most one pending context callback.
/// Filtering inside Progress&lt;T&gt;'s callback still queues every chunk on the UI thread.</summary>
public sealed class CoalescingProgress<T>(Action<T> handler) : IProgress<T>
{
    private readonly SynchronizationContext _context = SynchronizationContext.Current ?? new SynchronizationContext();
    private readonly object _gate = new();
    private T _latest = default!;
    private bool _queued;

    public void Report(T value)
    {
        lock (_gate)
        {
            _latest = value;
            if (_queued) return;
            _queued = true;
        }
        _context.Post(_ => Drain(), null);
    }

    private void Drain()
    {
        T value;
        lock (_gate) { value = _latest; _queued = false; }
        handler(value);
    }
}
