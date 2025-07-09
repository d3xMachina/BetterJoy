namespace BetterJoy.Hardware.SubCommand;

public enum BatteryLevel : byte
{
    Empty = 0x00,
    Critical = 0x02,
    Low = 0x04,
    Medium = 0x06,
    Full = 0x08,
    Unknown = 0xFF,
}
