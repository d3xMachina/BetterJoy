using System.Net;

namespace BetterJoy.Config;

public class ProgramConfig : Config
{
    public bool UseHIDHide = true;
    public bool HIDHideAlwaysOn = false;
    public bool PurgeWhitelist = false;
    public bool PurgeAffectedDevices = false;
    public bool MotionServer = true;
    public IPAddress IP = IPAddress.Loopback;
    public int Port = 26760;

    public ProgramConfig(Logger logger) : base(logger) { }

    public ProgramConfig(ProgramConfig config) : base(config._logger)
    {
        UseHIDHide = config.UseHIDHide;
        HIDHideAlwaysOn = config.HIDHideAlwaysOn;
        PurgeWhitelist = config.PurgeWhitelist;
        PurgeAffectedDevices = config.PurgeAffectedDevices;
        MotionServer = config.MotionServer;
        IP = config.IP;
        Port = config.Port;
    }

    public override void Update()
    {
        TryUpdateSetting("UseHidHide", ref UseHIDHide);
        TryUpdateSetting("HIDHideAlwaysOn", ref HIDHideAlwaysOn);
        TryUpdateSetting("PurgeWhitelist", ref PurgeWhitelist);
        TryUpdateSetting("PurgeAffectedDevices", ref PurgeAffectedDevices);
        TryUpdateSetting("MotionServer", ref MotionServer);
        TryUpdateSetting("IP", ref IP);
        TryUpdateSetting("Port", ref Port);
    }

    public override ProgramConfig Clone()
    {
        return new ProgramConfig(this);
    }
}
