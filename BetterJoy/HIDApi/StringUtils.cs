using BetterJoy.Memory;
using System;
using System.Text;

namespace BetterJoy.HIDApi;

internal static class StringUtils
{
    private const int MaxStringLength = 200; // Value from MAX_DEVICE_ID_LEN in cfgmgr32.h, it includes the null character
    private const int UnicodeBufferSize = MaxStringLength * sizeof(ushort);

    public delegate int GetStringDelegate(Span<byte> b, nuint length);

    public static string GetUnicodeString(GetStringDelegate getStringFunc)
    {
        using var bufferOwner = ArrayPoolHelper<byte>.Shared.Rent(UnicodeBufferSize);
        var buffer = bufferOwner.Span;

        var ret = getStringFunc(buffer, MaxStringLength);
        if (ret < 0)
        {
            return string.Empty;
        }

        var nullTerminatorPosition = FindNullTerminator(buffer);
        return Encoding.Unicode.GetString(buffer[..nullTerminatorPosition]);
    }

    private static int FindNullTerminator(ReadOnlySpan<byte> buffer)
    {
        for (int i = 0; i < buffer.Length - 1; i += 2)
        {
            if (buffer[i] == 0 && buffer[i + 1] == 0)
            {
                return i;
            }
        }

        return buffer.Length; // No terminator found
    }
}
