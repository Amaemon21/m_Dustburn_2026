public class MinHeap
{
    private int[] _items;
    private float[] _priorities;
    private int _count;

    public int Count => _count;

    public MinHeap(int capacity)
    {
        _items = new int[capacity < 4 ? 4 : capacity];
        _priorities = new float[_items.Length];
    }

    public void Clear()
    {
        _count = 0;
    }

    public void Push(int item, float priority)
    {
        if (_count == _items.Length)
            Grow();

        int index = _count++;

        _items[index] = item;
        _priorities[index] = priority;

        while (index > 0)
        {
            int parent = (index - 1) / 2;

            if (_priorities[parent] <= _priorities[index])
                break;

            Swap(parent, index);
            index = parent;
        }
    }

    public bool TryPop(out int item)
    {
        if (_count == 0)
        {
            item = 0;
            return false;
        }

        item = _items[0];
        _count--;

        if (_count == 0)
            return true;

        _items[0] = _items[_count];
        _priorities[0] = _priorities[_count];

        int index = 0;

        while (true)
        {
            int left = index * 2 + 1;
            int right = left + 1;
            int smallest = index;

            if (left < _count && _priorities[left] < _priorities[smallest])
                smallest = left;

            if (right < _count && _priorities[right] < _priorities[smallest])
                smallest = right;

            if (smallest == index)
                break;

            Swap(smallest, index);
            index = smallest;
        }

        return true;
    }

    private void Grow()
    {
        int capacity = _items.Length * 2;

        var items = new int[capacity];
        var priorities = new float[capacity];

        System.Array.Copy(_items, items, _count);
        System.Array.Copy(_priorities, priorities, _count);

        _items = items;
        _priorities = priorities;
    }

    private void Swap(int a, int b)
    {
        (_items[a], _items[b]) = (_items[b], _items[a]);
        (_priorities[a], _priorities[b]) = (_priorities[b], _priorities[a]);
    }
}
