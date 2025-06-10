using BetterJoy.Controller;
using System.Numerics;

namespace BetterJoy.Network.Server;

public readonly record struct MotionData(Vector3 Gyro, Vector3 Accel);

public class UdpControllerReport
{
    public ulong Timestamp;
    public int PacketCounter;
    public ulong DeltaPackets;

    public int PadId;
    public MacAddress MacAddress;
    public ControllerConnection ConnectionType;
    public ControllerBattery Battery;

    public OutputControllerDualShock4InputState Input;

    public readonly MotionData[] Motion = new MotionData[3];
}
