using BetterJoy.Hardware.Data;
using System;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Forms.VisualStyles;

namespace BetterJoy.Hardware.SubCommandUtils;

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

    protected readonly ResponseBuffer Raw;

    protected IncomingPacket(ReadOnlySpan<byte> buffer)
    {
        buffer.CopyTo(Raw);
    }
    
    public byte MessageCode => Raw[ResponseCodeIndex];
    public byte Timer => Raw[TimerIndex];
    public BatteryLevel BatteryLevel => (BatteryLevel)BitWrangler.UpperNibble(Raw[BatteryAndConnectionIndex]);
    public bool IsCharging => (Raw[BatteryAndConnectionIndex] & 1) > 0;
    
    //TODO: Clean this up once we have objects to represent the inputs
    public ReadOnlySpan<byte> InputData => ((ReadOnlySpan<byte>)Raw)[ButtonStateStartIndex..(RumbleStateIndex - ButtonStateStartIndex)];
    
    public override string ToString()
    {
        var output = new StringBuilder();
        
        output.Append($" Message Code: {MessageCode:X2}");
        output.Append($" Timer: {Timer:X2}");
        output.Append($" Battery Level: {BatteryLevel.ToString()}");
        output.Append($" Charging: {(IsCharging ? "Yes" : "no")}");
        output.Append($" Input Data: ");
        
        foreach (var inputByte in InputData)
        {
            output.Append($" {inputByte:X2}");
        }

        return output.ToString();
    }
}
