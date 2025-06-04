using System;
using System.Buffers;
using System.Threading;

namespace BetterJoy.Memory;

public sealed partial class ArrayPoolHelper<T>
{
    public interface IArrayOwner<U> : IDisposable
    {
        int Length { get; }
        U[] Array { get; }
        Span<U> Span { get; }
        ReadOnlyMemory<U> ReadOnlyMemory { get; }
    }

    private sealed class ArrayOwner<U> : IArrayOwner<U>
    {
        private readonly int _length;
        private U[] _array;

        public ArrayOwner(int length)
        {
            _array = ArrayPool<U>.Shared.Rent(length);
            _length = length;
        }

        public int Length => _length;
        public U[] Array => _array; // carefull, length allocated to the array might be bigger than demanded, prefer to use Span instead
        public Span<U> Span => _array.AsSpan(0, _length);
        public ReadOnlyMemory<U> ReadOnlyMemory => new ReadOnlyMemory<U>(_array, 0, Length);

        public void Dispose()
        {
            var array = Interlocked.Exchange(ref _array, null);

            if (array != null)
            {
                ArrayPool<U>.Shared.Return(array);
            }
        }
    }
}
