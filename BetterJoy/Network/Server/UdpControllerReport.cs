using BetterJoy.Controller;
using BetterJoy.Controller.Mapping;

namespace BetterJoy.Network.Server;

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

    public readonly Motion[] Motion = new Motion[3];

    public static UdpControllerReport? ConstructIfServerIsServing(Joycon controller, ulong deltaPackets = 0)
    {
        return Program.Server != null && Program.Server.HasClients 
            ? new UdpControllerReport(controller, deltaPackets) 
            : null;
    }

    private UdpControllerReport(Joycon controller, ulong deltaPackets = 0)
    {
        Timestamp = controller.Timestamp;
        PacketCounter = controller.PacketCounter;
        DeltaPackets = deltaPackets;
        PadId = controller.PadId;
        MacAddress = controller.MacAddress;
        ConnectionType = controller.IsUSB ? ControllerConnection.USB : ControllerConnection.Bluetooth;
        Battery = UdpUtils.GetBattery(controller);
    }

    public void SendControllerReport() => Program.Server.SendControllerReport(this);

    public void AddInput(Joycon controller)
    {
        Input = controller.MapToDualShock4Input();

        // Invert Y axis
        Input.ThumbLeftY = (byte)(byte.MaxValue - Input.ThumbLeftY);
        Input.ThumbRightY = (byte)(byte.MaxValue - Input.ThumbRightY);
    }

    public void AddMotion(Joycon controller, int packetNumber)
    {
        Motion[packetNumber] = controller.GetMotion();
    }
}
