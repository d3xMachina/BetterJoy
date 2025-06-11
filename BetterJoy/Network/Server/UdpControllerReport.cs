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

    public UdpControllerReport(Joycon controller, ulong deltaPackets = 0)
    {
        Timestamp = controller.Timestamp;
        PacketCounter = controller.PacketCounter;
        DeltaPackets = deltaPackets;
        PadId = controller.PadId;
        MacAddress = controller.MacAddress;
        ConnectionType = controller.IsUSB ? ControllerConnection.USB : ControllerConnection.Bluetooth;
        Battery = UdpUtils.GetBattery(controller);
    }

    public void AddInput(Joycon controller)
    {
        Input = Joycon.MapToDualShock4Input(controller);

        // Invert Y axis
        Input.ThumbLeftY = (byte)(byte.MaxValue - Input.ThumbLeftY);
        Input.ThumbRightY = (byte)(byte.MaxValue - Input.ThumbRightY);
    }

    public void AddMotion(Joycon controller, int packetNumber)
    {
        Motion[packetNumber] = new MotionData(controller.GetGyro(), controller.GetAccel());
    }
}
