using System;
using static BetterJoy.Joycon;

namespace BetterJoy.Config;

public class ControllerConfig : Config
{
    public int LowFreq;
    public int HighFreq;
    public bool EnableRumble;
    public bool ShowAsXInput;
    public bool ShowAsDs4;
    public float StickLeftDeadzone;
    public float StickRightDeadzone;
    public float StickLeftRange;
    public float StickRightRange;
    public bool SticksSquared;
    public float[] StickLeftAntiDeadzone = new float[2];
    public float[] StickRightAntiDeadzone = new float[2];
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
    public int[] GyroMouseSensitivity = new int[2];
    public float GyroStickReduction;
    public float[] GyroStickSensitivity = new float[2];
    public bool HomeLongPowerOff;
    public bool HomeLEDOn;
    public long PowerOffInactivityMins;
    public bool SwapAB;
    public bool SwapXY;
    public bool MinusToShare;
    public bool UseFilteredIMU;
    public DebugType DebugType;
    public Orientation DoNotRejoin;
    public bool AutoPowerOff;
    public bool AllowCalibration;

    public ControllerConfig(Logger logger) : base(logger) { }

    public ControllerConfig(ControllerConfig config) : base(config._logger)
    {
        LowFreq = config.LowFreq;
        HighFreq = config.HighFreq;
        EnableRumble = config.EnableRumble;
        ShowAsXInput = config.ShowAsXInput;
        ShowAsDs4 = config.ShowAsDs4;
        StickLeftDeadzone = config.StickLeftDeadzone;
        StickRightDeadzone = config.StickRightDeadzone;
        StickLeftRange = config.StickLeftRange;
        StickRightRange = config.StickRightRange;
        SticksSquared = config.SticksSquared;
        Array.Copy(config.StickLeftAntiDeadzone, StickLeftAntiDeadzone, StickLeftAntiDeadzone.Length);
        Array.Copy(config.StickRightAntiDeadzone, StickRightAntiDeadzone, StickRightAntiDeadzone.Length);
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
        Array.Copy(config.GyroMouseSensitivity, GyroMouseSensitivity, GyroMouseSensitivity.Length);
        GyroStickReduction = config.GyroStickReduction;
        Array.Copy(config.GyroStickSensitivity, GyroStickSensitivity, GyroStickSensitivity.Length);
        HomeLongPowerOff = config.HomeLongPowerOff;
        HomeLEDOn = config.HomeLEDOn;
        PowerOffInactivityMins = config.PowerOffInactivityMins;
        SwapAB = config.SwapAB;
        SwapXY = config.SwapXY;
        MinusToShare = config.MinusToShare;
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
        UpdateSetting("StickLeftDeadzone", ref StickLeftDeadzone, 0.15f);
        UpdateSetting("StickRightDeadzone", ref StickRightDeadzone, 0.15f);
        UpdateSetting("StickLeftRange", ref StickLeftRange, 0.90f);
        UpdateSetting("StickRightRange", ref StickRightRange, 0.90f);
        UpdateSetting("SticksSquared", ref SticksSquared, false);
        UpdateSetting("StickLeftAntiDeadzone", ref StickLeftAntiDeadzone, [0.0f, 0.0f]);
        UpdateSetting("StickRightAntiDeadzone", ref StickRightAntiDeadzone, [0.0f, 0.0f]);
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
        UpdateSetting("GyroMouseSensitivity", ref GyroMouseSensitivity, [1200, 800]);
        UpdateSetting("GyroStickReduction", ref GyroStickReduction, 1.5f);
        UpdateSetting("GyroStickSensitivity", ref GyroStickSensitivity, [40.0f, 10.0f]);
        UpdateSetting("HomeLongPowerOff", ref HomeLongPowerOff, true);
        UpdateSetting("HomeLEDOn", ref HomeLEDOn, true);
        UpdateSetting("PowerOffInactivity", ref PowerOffInactivityMins, -1);
        UpdateSetting("SwapAB", ref SwapAB, false);
        UpdateSetting("SwapXY", ref SwapXY, false);
        UpdateSetting("MinusToShare", ref MinusToShare, false);
        UpdateSetting("UseFilteredIMU", ref UseFilteredIMU, true);
        UpdateSetting("DebugType", ref DebugType, DebugType.None);
        UpdateSetting("DoNotRejoinJoycons", ref DoNotRejoin, Orientation.None);
        UpdateSetting("AutoPowerOff", ref AutoPowerOff, false);
        UpdateSetting("AllowCalibration", ref AllowCalibration, true);
    }

    public override ControllerConfig Clone()
    {
        return new ControllerConfig(this);
    }
}
