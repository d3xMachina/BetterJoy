using System;

namespace BetterJoy.Memory;

public sealed partial class ArrayPoolHelper<T>
{
    private ArrayPoolHelper() { }

    public static ArrayPoolHelper<T> Shared { get; } = new();
    public static int MaxBufferSize => Array.MaxLength;

    public IArrayOwner<T> Rent(int length)
    {
        return RentImpl(length);
    }

    public IArrayOwner<T> RentCleared(int length)
    {
        var buffer = RentImpl(length);
        Array.Clear(buffer.Array);

        return buffer;
    }

    private static ArrayOwner<T> RentImpl(int length)
    {
        if (length > MaxBufferSize)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, null);
        }

        return new ArrayOwner<T>(length);
    }
}
