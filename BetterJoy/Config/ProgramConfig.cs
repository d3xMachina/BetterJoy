using System.Net;

namespace BetterJoy.Config;

public class ProgramConfig : Config
{
    public bool UseHIDHide;
    public bool HIDHideAlwaysOn;
    public bool PurgeWhitelist;
    public bool PurgeAffectedDevices;
    public bool MotionServer;
    public IPAddress IP;
    public int Port;

    public ProgramConfig(MainForm form) : base(form) { }

    public ProgramConfig(ProgramConfig config) : base (config._form)
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
        UpdateSetting("UseHidHide", ref UseHIDHide, true);
        UpdateSetting("HIDHideAlwaysOn", ref HIDHideAlwaysOn, false);
        UpdateSetting("PurgeWhitelist", ref PurgeWhitelist, false);
        UpdateSetting("PurgeAffectedDevices", ref PurgeAffectedDevices, false);
        UpdateSetting("MotionServer", ref MotionServer, true);
        UpdateSetting("IP", ref IP, IPAddress.Loopback);
        UpdateSetting("Port", ref Port, 26760);
    }

    public override ProgramConfig Clone()
    {
        return new ProgramConfig(this);
    }
}