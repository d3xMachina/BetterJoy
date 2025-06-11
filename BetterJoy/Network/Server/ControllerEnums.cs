namespace BetterJoy.Network.Server;

public enum ControllerState : byte
{
    Disconnected = 0x00,
    Connected = 0x02
};

public enum ControllerConnection : byte
{
    None = 0x00,
    USB = 0x01,
    Bluetooth = 0x02
};

public enum ControllerModel : byte
{
    None = 0x00,
    DS3 = 0x01,
    DS4 = 0x02,
    Generic = 0x03
}

public enum ControllerBattery : byte
{
    Empty = 0x00,
    Critical = 0x01,
    Low = 0x02,
    Medium = 0x03,
    High = 0x04,
    Full = 0x05,
    Charging = 0xEE,
    Charged = 0xEF
};
