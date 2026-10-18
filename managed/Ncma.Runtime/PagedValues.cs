namespace Ncma.Runtime;

// Fixed-capacity, lazily allocated pages. Sharing revokes BOTH writers; no reference counts,
// user leases or in-place changes to a page belonging to another candidate are exposed.
internal sealed class PagedValues<T> where T : struct
{
    internal const int DefaultPageSize = 32;
    private sealed record Page(object Owner, T[] Values);
    private Page?[] _pages;
    private object _owner = new();
    private bool _directoryOwned = true;
    private readonly int _shift, _mask;

    internal PagedValues(int pageSize = DefaultPageSize)
    {
        if (pageSize < 16 || pageSize > 256 || (pageSize & (pageSize - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        _shift = System.Numerics.BitOperations.Log2((uint)pageSize); _mask = pageSize - 1;
        _pages = new Page?[World.MaxObjects / pageSize];
    }
    private PagedValues(Page?[] pages, int shift, int mask)
    { _pages = pages; _shift = shift; _mask = mask; _directoryOwned = false; }

    internal PagedValues<T> Fork()
    {
        var copy = new PagedValues<T>(_pages, _shift, _mask);
        var nextOwner = new object(); // All allocations before revoking the parent's ownership.
        _owner = nextOwner; _directoryOwned = false;
        return copy;
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal T Read(int index)
    {
        if ((uint)index >= World.MaxObjects) throw new ArgumentOutOfRangeException(nameof(index));
        var page = _pages[index >> _shift];
        return page is null ? default : page.Values[index & _mask];
    }
    // Pool membership has already proved this initialized dense value exists.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal T ReadPresent(int index) => _pages[index >> _shift]!.Values[index & _mask];
    // Multi-cell structural operations prepare every potentially allocating write FIRST.
    internal void PrepareWrite(int index) => _ = Writable(index);
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal void Write(int index, T value) => Writable(index)[index & _mask] = value;

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private T[] Writable(int index)
    {
        if ((uint)index >= World.MaxObjects) throw new ArgumentOutOfRangeException(nameof(index));
        int number = index >> _shift;
        var prior = _pages[number];
        if (prior is not null && ReferenceEquals(prior.Owner, _owner)) return prior.Values;
        return CopyPage(number, prior);
    }
    private T[] CopyPage(int number, Page? prior)
    {
        var values = prior is null ? new T[_mask + 1] : (T[])prior.Values.Clone();
        var page = new Page(_owner, values);
        var directory = _directoryOwned ? _pages : (Page?[])_pages.Clone();
        directory[number] = page;
        _pages = directory; _directoryOwned = true;
        return values;
    }
}
