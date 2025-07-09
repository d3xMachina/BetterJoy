#nullable enable
using BetterJoy.Hardware.Data;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace BetterJoy.Hardware.SubCommand;

public class SubCommandReturnPacket : IncomingPacket
{
    protected const int SubCommandOperationIndex = 14;
    protected const int PayloadStartIndex = 15;
    public const int MinimumSubcommandReplySize = 20;

    protected const int SubCommandReturnPacketResponseCode = 0x21;

    public static bool TryConstruct(
        SubCommandOperation operation,
        ReadOnlySpan<byte> buffer,
        [NotNullWhen(true)]
        out SubCommandReturnPacket? packet)
    {
        try
        {
            packet = new SubCommandReturnPacket(operation, buffer);

            return true;
        }
        catch (ArgumentException)
        {
            packet = null;

            return false;
        }
    }

    protected SubCommandReturnPacket(SubCommandOperation operation, ReadOnlySpan<byte> buffer) : base(buffer)
    {
        if (!IsValidSubCommandReturnPacket(operation, buffer))
        {
            throw new ArgumentException("Provided array is not valid subcommand response for given operation.");
        }
    }

    private static bool IsValidSubCommandReturnPacket(SubCommandOperation operation, ReadOnlySpan<byte> buffer)
    {
        return buffer.Length >= MinimumSubcommandReplySize &&
               buffer[ResponseCodeIndex] == SubCommandReturnPacketResponseCode &&
               buffer[SubCommandOperationIndex] == (byte)operation;
    }

    public bool IsSubCommandReply => Raw[ResponseCodeIndex] == SubCommandReturnPacketResponseCode;


    public SubCommandOperation SubCommandOperation =>
        BitWrangler.ByteToEnumOrDefault(
            BitWrangler.UpperNibble(Raw[SubCommandOperationIndex]), SubCommandOperation.Unknown);

    public ReadOnlySpan<byte> Payload => Raw[PayloadStartIndex..];


    public override string ToString()
    {
        var output = new StringBuilder();

        output.Append($"Subcommand Echo: {(byte)SubCommandOperation:X2} ");

        if (!Payload.IsEmpty)
        {
            output.Append(" Payload:");

            foreach (var dataByte in Payload)
            {
                output.Append($" {dataByte:X2}");
            }
        }

        return output.ToString();
    }
}
