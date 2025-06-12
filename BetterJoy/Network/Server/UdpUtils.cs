using BetterJoy.Controller;
using System;
using System.IO.Hashing;

namespace BetterJoy.Network.Server;
public static class UdpUtils
{
    public static ControllerBattery GetBattery(Joycon controller)
    {
        if (controller.Charging)
        {
            return ControllerBattery.Charging;
        }

        return controller.Battery switch
        {
            Joycon.BatteryLevel.Critical => ControllerBattery.Critical,
            Joycon.BatteryLevel.Low => ControllerBattery.Low,
            Joycon.BatteryLevel.Medium => ControllerBattery.Medium,
            Joycon.BatteryLevel.Full => ControllerBattery.Full,
            _ => ControllerBattery.Empty,
        };
    }

    public static int CalculateCrc32(ReadOnlySpan<byte> data, Span<byte> crc)
    {
        return Crc32.Hash(data, crc);
    }

    public static uint CalculateCrc32(ReadOnlySpan<byte> data)
    {
        Span<byte> crc = stackalloc byte[4];
        Crc32.Hash(data, crc);
        return BitConverter.ToUInt32(crc);
    }
}
