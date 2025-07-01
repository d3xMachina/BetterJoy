using System;
using System.Runtime.CompilerServices;

namespace BetterJoy.Hardware.Data;

public static class BitWrangler
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte LowerNibble(byte b) => (byte)(b & 0x0F);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte UpperNibble(byte b) => (byte)(b >> 4);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte LowerToUpper(byte b) => (byte)(b << 4);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte EncodeNibblesAsByteLittleEndian(byte low, byte high)
        => (byte)(LowerToUpper(high) | LowerNibble(low));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort EncodeBytesAsWordLittleEndian(byte low, byte high)
        => (ushort)((high << 8) | low);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static short EncodeBytesAsWordLittleEndianSigned(byte low, byte high)
        => (short)((high << 8) | low);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort Lower3Nibbles(ushort word)
        => (ushort)(word & 0x0FFF);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort Upper3Nibbles(ushort word)
        => (ushort)(word >> 4);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort Lower3NibblesLittleEndian(byte low, byte high)
        => Lower3Nibbles(EncodeBytesAsWordLittleEndian(low, high));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort Upper3NibblesLittleEndian(byte low, byte high)
        => Upper3Nibbles(EncodeBytesAsWordLittleEndian(low, high));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort InvertWord(ushort word)
        => (ushort)(ushort.MaxValue - word);
    
    public static TEnum ByteToEnumOrDefault<TEnum>(byte value, TEnum defaultValue)
        where TEnum : struct, Enum
    {
        return Enum.IsDefined(typeof(TEnum), value)
            ? Unsafe.As<byte, TEnum>(ref value)
            : defaultValue;
    }
}
