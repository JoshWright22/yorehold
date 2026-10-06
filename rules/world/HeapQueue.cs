namespace Yorehold.Rules;

/// <summary>
/// A binary heap that pushes and pops the way the C++ client's std::priority_queue does, so
/// entries with the same priority come out in the same order and paths agree cell for cell.
/// .NET's PriorityQueue is a 4-ary heap and breaks ties differently.
/// </summary>
public sealed class HeapQueue<T>
{
    private readonly List<T> _items = new();
    // True when a should sit below b (std::greater for a min-heap).
    private readonly Func<T, T, bool> _below;

    public HeapQueue(Func<T, T, bool> below)
    {
        _below = below;
    }

    public int Count => _items.Count;

    public T Top => _items[0];

    public void Push(T item)
    {
        _items.Add(item);
        PushHole(_items.Count - 1, 0, item);
    }

    public T Pop()
    {
        T top = _items[0];
        int last = _items.Count - 1;
        if (last >= 1)
        {
            T moved = _items[last];
            _items[last] = top;
            PopHole(0, last, moved);
        }
        _items.RemoveAt(last);
        return top;
    }

    // Moves the hole down to a leaf by the better child, then sifts the value up from there.
    private void PopHole(int hole, int bottom, T value)
    {
        int top = hole;
        int index = hole;
        int lastParent = (bottom - 1) >> 1;
        while (index < lastParent)
        {
            index = 2 * index + 2;
            if (_below(_items[index], _items[index - 1]))
            {
                index--;
            }
            _items[hole] = _items[index];
            hole = index;
        }
        if (index == lastParent && bottom % 2 == 0)
        {
            _items[hole] = _items[bottom - 1];
            hole = bottom - 1;
        }
        PushHole(hole, top, value);
    }

    private void PushHole(int hole, int top, T value)
    {
        for (int parent = (hole - 1) >> 1; top < hole && _below(_items[parent], value); parent = (hole - 1) >> 1)
        {
            _items[hole] = _items[parent];
            hole = parent;
        }
        _items[hole] = value;
    }
}
