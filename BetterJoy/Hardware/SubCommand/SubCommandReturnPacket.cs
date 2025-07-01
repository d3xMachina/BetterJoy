using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace BetterJoy.Hardware.SubCommand;

public class SubCommandReturnPacket : IncomingPacket
{
    protected const int AckIndex = 13;
    protected const int SubCommandEchoIndex = 14;
    protected const int PayloadStartIndex = 15;
    
    protected const int SubCommandReturnPacketResponseCode = 0x21;
    
    public static bool TryConstruct(
        SubCommandOperation operation,
        ReadOnlySpan<byte> buffer, 
        int length,
        [NotNullWhen(true)]
        out SubCommandReturnPacket? packet)
    {
        bool valid = IsValidSubCommandReturnPacket(operation, buffer, length);
        
        packet = valid ? new SubCommandReturnPacket(operation, buffer, length) : null;

        return valid;
    }

    protected SubCommandReturnPacket(SubCommandOperation operation, ReadOnlySpan<byte> buffer, int length) : base(buffer, length)
    {
        if (!IsValidSubCommandReturnPacket(operation, buffer, length))
        {
            throw new ArgumentException("Provided array is not valid subcommand response for given operation.");
        }
    }

    private static bool IsValidSubCommandReturnPacket(SubCommandOperation operation, ReadOnlySpan<byte> buffer, int length)
    {
        return length >= PayloadStartIndex + 5 && 
               buffer[ResponseCodeIndex] == SubCommandReturnPacketResponseCode &&
               buffer[SubCommandEchoIndex] == (byte)operation;
    }
    
    public bool IsSubCommandReply => Raw[ResponseCodeIndex] == SubCommandReturnPacketResponseCode;
    
    public bool SubCommandSucceeded => Raw[AckIndex] == 0x01;
    public SubCommandOperation Operation => Enum.IsDefined(
        typeof(SubCommandOperation), 
        Raw[SubCommandEchoIndex] is var subCommandByte) 
        ? (SubCommandOperation) subCommandByte 
        : SubCommandOperation.Unknown;
    public ReadOnlySpan<byte> Payload => Raw[PayloadStartIndex..];
    
    
    public override string ToString()
    {
        var output = new StringBuilder();

        output.Append($"Subcommand Echo: {(byte)Operation:X2} ({(SubCommandSucceeded ? "Success" : "Failure")})");
        output.Append($"Status: {(SubCommandSucceeded ? "Success" : "Failure")}");

        output.Append(base.ToString());

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
