using System.Runtime.CompilerServices;

namespace BetterJoy.Hardware;

public class Nibble
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte LowerNibble(byte b) => (byte)(b & 0x0F);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte UpperNibble(byte b) => (byte)(b >> 4);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte Merge(byte low, byte high)
        => (byte)(LowerNibble(high) | UpperNibble(low));
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort EncodeBytesLittleEndianUnsigned(byte low, byte high)
        => (ushort)((high << 8) | low);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static short EncodeBytesLittleEndian(byte low, byte high)
        => (short)((high << 8) | low);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort Lower3Nibbles(ushort word)
        => (ushort)(word & 0x0FFF);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort Upper3Nibbles(ushort word)
        => (ushort)(word >> 4);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort Lower3NibblesLittleEndian(byte low, byte high)
        => Lower3Nibbles(EncodeBytesLittleEndianUnsigned(low, high));
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort Upper3NibblesLittleEndian(byte low, byte high)
        => Upper3Nibbles(EncodeBytesLittleEndianUnsigned(low, high));
}
