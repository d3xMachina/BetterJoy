#nullable enable
using BetterJoy.Hardware.Data;
using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace BetterJoy.Hardware.SubCommand;

public class SubCommandPacket
{
    private static readonly byte[] _stopRumbleBuf = [0x0, 0x1, 0x40, 0x40, 0x0, 0x1, 0x40, 0x40]; // Stop rumble
    private static readonly byte[] _ignoreRumbleBuf = [0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0]; // Ignore rumble

    private const int PacketStartIndex = 0;
    private const int CommandCountIndex = 1;
    private const int RumbleContentsStartIndex = 2;
    private const int CommandIndex = 10;
    private const int ArgumentsStartIndex = 11;
    private const int BluetoothPacketSize = 49;
    private const int USBPacketSize = 64;
    private const int RumbleLength = CommandIndex - RumbleContentsStartIndex;

    private readonly int _packetSize;
    private readonly int _argsLength;
    private int MaxArgsLength => _packetSize - ArgumentsStartIndex;

    [InlineArray(USBPacketSize)]
    private struct CommandBuffer
    {
        private byte _firstElement;
    }

    private readonly CommandBuffer _raw;

    public SubCommandPacket(
        SubCommandOperation subCommandOperation,
        byte commandCount,
        ReadOnlySpan<byte> args = default,
        ReadOnlySpan<byte> rumble = default,
        bool useUSBPacketSize = false)
    {
        _packetSize = useUSBPacketSize ? USBPacketSize : BluetoothPacketSize;

        // Default to stopping the rumble
        if (rumble.IsEmpty)
        {
            rumble = _stopRumbleBuf;
        }
        else if (rumble.Length != RumbleLength) // Check the rumble length if user provided
        {
            throw new ArgumentException($@"Rumble span is not correct size. Expected: {RumbleLength} Received: {rumble.Length}", nameof(rumble));
        }

        // Check the args length
        if (args.Length > MaxArgsLength)
        {
            throw new ArgumentException($@"Args span is too large. Expected at most: {MaxArgsLength} Received: {args.Length}", nameof(args));
        }

        _argsLength = args.Length;
        _raw[PacketStartIndex] = 0x01; // Always
        _raw[CommandCountIndex] = BitWrangler.LowerNibble(commandCount); // Command index only uses 4 bits
        _raw[CommandIndex] = (byte)subCommandOperation;

        rumble.CopyTo(_raw[RumbleContentsStartIndex..]);
        args.CopyTo(_raw[ArgumentsStartIndex..]);
    }

    public static implicit operator ReadOnlySpan<byte>(SubCommandPacket subCommand) => subCommand._raw[..subCommand._packetSize];

    public SubCommandOperation Operation => (SubCommandOperation)_raw[CommandIndex];

    public ReadOnlySpan<byte> Arguments => ((ReadOnlySpan<byte>)_raw).Slice(ArgumentsStartIndex, _argsLength);

    public ReadOnlySpan<byte> Rumble => ((ReadOnlySpan<byte>)_raw).Slice(RumbleContentsStartIndex, RumbleLength);

    public override string ToString()
    {
        var output = new StringBuilder();

        output.Append($"Subcommand {(byte)Operation:X2} sent.");

        if (_argsLength > 0)
        {
            output.Append(" Data:");

            foreach (var arg in Arguments)
            {
                output.Append($" {arg:X2}");
            }
        }

        if (!Rumble.SequenceEqual(_ignoreRumbleBuf.AsSpan()))
        {
            output.Append(" Rumble:");

            if (Rumble.SequenceEqual(_stopRumbleBuf.AsSpan()))
            {
                output.Append(" <Stop Sequence>");
            }
            else
            {
                foreach (var rumbleVal in Rumble)
                {
                    output.Append($" {rumbleVal:X2}");
                }
            }
        }

        return output.ToString();
    }
}
