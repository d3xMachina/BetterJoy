namespace BetterJoy.Hardware.SubCommandUtils;

public enum BatteryLevel : byte
{
    Empty = 0x0,
    Critical = 0x2,
    Low = 0x4,
    Medium = 0x6,
    Full = 0x8
}
