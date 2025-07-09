using BetterJoy.Hardware.Data;
using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace BetterJoy.Hardware.SubCommand;

public abstract class IncomingPacket
{
    protected const int ResponseCodeIndex = 0;
    protected const int TimerIndex = 1;
    protected const int BatteryAndConnectionIndex = 2;
    protected const int ButtonStateStartIndex = 3;
    protected const int StickStateStartIndex = 6;
    protected const int RumbleStateIndex = 12;

    private const int USBPacketSize = 64;

    [InlineArray(USBPacketSize)]
    protected struct ResponseBuffer
    {
        private byte _firstElement;
    }

    private readonly ResponseBuffer _raw;
    private readonly int _length;
    protected ReadOnlySpan<byte> Raw => _raw[.._length];

    protected IncomingPacket(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < RumbleStateIndex)
        {
            throw new ArgumentException($"Provided length cannot be less than {RumbleStateIndex}.");
        }

        _length = buffer.Length;

        buffer.CopyTo(_raw);
    }

    public byte MessageCode => Raw[ResponseCodeIndex];

    public int Length => Raw.Length;

    public override string ToString()
    {
        var output = new StringBuilder();

        output.Append($" Message Code: {MessageCode:X2}");
        output.Append($" Data: ");

        foreach (var dataByte in Raw[TimerIndex..])
        {
            output.Append($" {dataByte:X2}");
        }

        return output.ToString();
    }
}
