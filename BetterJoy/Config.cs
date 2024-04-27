using System;
using System.Configuration;
using System.Net;
using System.Reflection;
using static BetterJoy.Joycon;

namespace BetterJoy
{
    public abstract class Config
    {
        protected MainForm _form;
        public bool ShowErrors = true;

        protected Config(MainForm form)
        {
            _form = form;
        }

        protected Config(Config config) : this(config._form) { }
        public abstract void Update();
        public abstract Config Clone();
        
        protected void UpdateSetting<T>(string key, ref T setting, T defaultValue)
        {
            var value = ConfigurationManager.AppSettings[key];

            if (value != null)
            {
                try
                {
                    var type = typeof(T);
                    if (type.IsEnum)
                    {
                        setting = (T)Enum.Parse(type, value, true);
                    }
                    else if (type == typeof(string) || type is IConvertible || type.IsValueType)
                    {
                        setting = (T)Convert.ChangeType(value, type);
                    }
                    else
                    {
                        var method = type.GetMethod("Parse", BindingFlags.Static | BindingFlags.Public, new[] { typeof(string) });
                        setting = (T)method!.Invoke(null, [value])!;
                    }
                    return;
                }
                catch (FormatException) { }
                catch (InvalidCastException) { }
                catch (ArgumentException) { }
            }

            setting = defaultValue;

            if (ShowErrors)
            {
                _form.AppendTextBox($"Invalid value \"{value}\" for setting {key}! Using default value \"{defaultValue}\".");
            }
        }
    }

    public class ControllerConfig : Config
    {
        public int LowFreq;
        public int HighFreq;
        public bool EnableRumble;
        public bool ShowAsXInput;
        public bool ShowAsDs4;
        public float DefaultDeadzone;
        public float DefaultRange;
        public bool SticksSquared;
        public float AHRSBeta;
        public float ShakeDelay;
        public bool ShakeInputEnabled;
        public float ShakeSensitivity;
        public bool ChangeOrientationDoubleClick;
        public bool DragToggle;
        public string ExtraGyroFeature;
        public int GyroAnalogSensitivity;
        public bool GyroAnalogSliders;
        public bool GyroHoldToggle;
        public bool GyroLeftHanded;
        public int GyroMouseSensitivityX;
        public int GyroMouseSensitivityY;
        public float GyroStickReduction;
        public float GyroStickSensitivityX;
        public float GyroStickSensitivityY;
        public bool HomeLongPowerOff;
        public bool HomeLEDOn;
        public long PowerOffInactivityMins;
        public bool SwapAB;
        public bool SwapXY;
        public bool UseFilteredIMU;
        public DebugType DebugType;
        public bool DoNotRejoin;
        public bool AutoPowerOff;
        public bool AllowCalibration;

        public ControllerConfig(MainForm form) : base(form) { }

        public ControllerConfig(ControllerConfig config) : base(config._form)
        {
            LowFreq = config.LowFreq;
            HighFreq = config.HighFreq;
            EnableRumble = config.EnableRumble;
            ShowAsXInput = config.ShowAsXInput;
            ShowAsDs4 = config.ShowAsDs4;
            DefaultDeadzone = config.DefaultDeadzone;
            DefaultRange = config.DefaultRange;
            SticksSquared = config.SticksSquared;
            AHRSBeta = config.AHRSBeta;
            ShakeDelay = config.ShakeDelay;
            ShakeInputEnabled = config.ShakeInputEnabled;
            ShakeSensitivity = config.ShakeSensitivity;
            ChangeOrientationDoubleClick = config.ChangeOrientationDoubleClick;
            DragToggle = config.DragToggle;
            ExtraGyroFeature = config.ExtraGyroFeature;
            GyroAnalogSensitivity = config.GyroAnalogSensitivity;
            GyroAnalogSliders = config.GyroAnalogSliders;
            GyroHoldToggle = config.GyroHoldToggle;
            GyroLeftHanded = config.GyroLeftHanded;
            GyroMouseSensitivityX = config.GyroMouseSensitivityX;
            GyroMouseSensitivityY = config.GyroMouseSensitivityY;
            GyroStickReduction = config.GyroStickReduction;
            GyroStickSensitivityX = config.GyroStickSensitivityX;
            GyroStickSensitivityY = config.GyroStickSensitivityY;
            HomeLongPowerOff = config.HomeLongPowerOff;
            HomeLEDOn = config.HomeLEDOn;
            PowerOffInactivityMins = config.PowerOffInactivityMins;
            SwapAB = config.SwapAB;
            SwapXY = config.SwapXY;
            UseFilteredIMU = config.UseFilteredIMU;
            DebugType = config.DebugType;
            DoNotRejoin = config.DoNotRejoin;
            AutoPowerOff = config.AutoPowerOff;
            AllowCalibration = config.AllowCalibration;
        }

        public override void Update()
        {
            UpdateSetting("LowFreqRumble", ref LowFreq, 160);
            UpdateSetting("HighFreqRumble", ref HighFreq, 320);
            UpdateSetting("EnableRumble", ref EnableRumble, true);
            UpdateSetting("ShowAsXInput", ref ShowAsXInput, true);
            UpdateSetting("ShowAsDS4", ref ShowAsDs4, false);
            UpdateSetting("SticksDeadzone", ref DefaultDeadzone, 0.15f);
            UpdateSetting("SticksRange", ref DefaultRange, 0.90f);
            UpdateSetting("SticksSquared", ref SticksSquared, false);
            UpdateSetting("AHRS_beta", ref AHRSBeta, 0.05f);
            UpdateSetting("ShakeInputDelay", ref ShakeDelay, 200);
            UpdateSetting("EnableShakeInput", ref ShakeInputEnabled, false);
            UpdateSetting("ShakeInputSensitivity", ref ShakeSensitivity, 10);
            UpdateSetting("ChangeOrientationDoubleClick", ref ChangeOrientationDoubleClick, true);
            UpdateSetting("DragToggle", ref DragToggle, false);
            UpdateSetting("GyroToJoyOrMouse", ref ExtraGyroFeature, "none");
            UpdateSetting("GyroAnalogSensitivity", ref GyroAnalogSensitivity, 400);
            UpdateSetting("GyroAnalogSliders", ref GyroAnalogSliders, false);
            UpdateSetting("GyroHoldToggle", ref GyroHoldToggle, true);
            UpdateSetting("GyroLeftHanded", ref GyroLeftHanded, false);
            UpdateSetting("GyroMouseSensitivityX", ref GyroMouseSensitivityX, 1200);
            UpdateSetting("GyroMouseSensitivityY", ref GyroMouseSensitivityY, 800);
            UpdateSetting("GyroStickReduction", ref GyroStickReduction, 1.5f);
            UpdateSetting("GyroStickSensitivityX", ref GyroStickSensitivityX, 40.0f);
            UpdateSetting("GyroStickSensitivityY", ref GyroStickSensitivityY, 10.0f);
            UpdateSetting("HomeLongPowerOff", ref HomeLongPowerOff, true);
            UpdateSetting("HomeLEDOn", ref HomeLEDOn, true);
            UpdateSetting("PowerOffInactivity", ref PowerOffInactivityMins, -1);
            UpdateSetting("SwapAB", ref SwapAB, false);
            UpdateSetting("SwapXY", ref SwapXY, false);
            UpdateSetting("UseFilteredIMU", ref UseFilteredIMU, true);
            UpdateSetting("DebugType", ref DebugType, DebugType.None);
            UpdateSetting("DoNotRejoinJoycons", ref DoNotRejoin, false);
            UpdateSetting("AutoPowerOff", ref AutoPowerOff, false);
            UpdateSetting("AllowCalibration", ref AllowCalibration, true);
        }

        public override ControllerConfig Clone()
        {
            return new ControllerConfig(this);
        }
    }

    public class ProgramConfig : Config
    {
        public bool UseHIDHide;
        public bool PurgeWhitelist;
        public bool PurgeAffectedDevices;
        public bool MotionServer;
        public IPAddress IP;
        public int Port;

        public ProgramConfig(MainForm form) : base(form) { }

        public ProgramConfig(ProgramConfig config) : base (config._form)
        {
            UseHIDHide = config.UseHIDHide;
            PurgeWhitelist = config.PurgeWhitelist;
            PurgeAffectedDevices = config.PurgeAffectedDevices;
            MotionServer = config.MotionServer;
            IP = config.IP;
            Port = config.Port;
        }

        public override void Update()
        {
            UpdateSetting("UseHidHide", ref UseHIDHide, true);
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

    public class MainFormConfig : Config
    {
        public bool AllowCalibration;

        public MainFormConfig(MainForm form) : base(form) { }

        public MainFormConfig(MainFormConfig config) : base(config._form)
        {
            AllowCalibration = config.AllowCalibration;
        }

        public override void Update()
        {
            UpdateSetting("AllowCalibration", ref AllowCalibration, true);
        }

        public override MainFormConfig Clone()
        {
            return new MainFormConfig(this);
        }
    }
}
