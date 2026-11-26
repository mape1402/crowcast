namespace CrowCast.Observability.Internal;

internal sealed class BoundedObservationBuffer<T>(int capacity)
{
    private readonly Queue<T> _items = new(Math.Max(1, capacity));
    private readonly int _capacity = Math.Max(1, capacity);
    private readonly object _gate = new();

    public void Add(T item)
    {
        lock (_gate)
        {
            while (_items.Count >= _capacity)
            {
                _items.Dequeue();
            }

            _items.Enqueue(item);
        }
    }

    public IReadOnlyList<T> Snapshot()
    {
        lock (_gate)
        {
            return _items.ToArray();
        }
    }
}
