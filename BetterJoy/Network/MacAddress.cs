using System;
using System.Runtime.CompilerServices;

namespace BetterJoy.Network;

[InlineArray(6)]
public struct MacAddress
{
    private byte _firstElement;

    public override readonly string ToString()
    {
        ReadOnlySpan<byte> macBytes = this;

        // Each byte is represented by 2 hex characters
        Span<char> hexChars = stackalloc char[macBytes.Length * 2];
        
        for (int i = 0; i < macBytes.Length; i++)
        {
            byte b = macBytes[i];
            hexChars[i * 2] = ToHexChar((byte)(b >> 4)); // High nibble
            hexChars[i * 2 + 1] = ToHexChar((byte)(b & 0xF)); // Low nibble
        }
        
        return new string(hexChars);
    }

    private static char ToHexChar(byte value)
    {
        return (char)(value < 10 ? '0' + value : 'a' + value - 10);
    }
}
