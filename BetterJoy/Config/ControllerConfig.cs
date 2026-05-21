using BetterJoy.Logging;
using System;
using static BetterJoy.Controller.Joycon;

namespace BetterJoy.Config;

public class ControllerConfig : Config
{
    public int LowFreq = 160;
    public int HighFreq = 320;
    public bool EnableRumble = true;
    public bool ShowAsXInput = true;
    public bool ShowAsDs4 = false;
    public float StickLeftDeadzone = 0.15f;
    public float StickRightDeadzone = 0.15f;
    public float StickLeftRange = 0.90f;
    public float StickRightRange = 0.90f;
    public bool SticksSquared = false;
    public float[] StickLeftAntiDeadzone = [0.0f, 0.0f];
    public float[] StickRightAntiDeadzone = [0.0f, 0.0f];
    public float AHRSBeta = 0.05f;
    public float ShakeDelay = 200;
    public bool ShakeInputEnabled = false;
    public float ShakeSensitivity = 10;
    public bool ChangeOrientationDoubleClick = true;
    public bool DragToggle = false;
    public string ExtraGyroFeature = "none";
    public int GyroAnalogSensitivity = 400;
    public bool GyroAnalogSliders = false;
    public bool GyroHoldToggle = true;
    public bool GyroLeftHanded = false;
    public int[] GyroMouseSensitivity = [1200, 800];
    public float GyroStickReduction = 1.5f;
    public float[] GyroStickSensitivity = [40.0f, 10.0f];
    public bool HomeLongPowerOff = true;
    public bool HomeLEDOn = true;
    public long PowerOffInactivityMins = -1;
    public bool SwapAB = false;
    public bool SwapXY = false;
    public bool MinusToShare = false;
    public bool UseFilteredMotion = true;
    public DebugType DebugType = DebugType.None;
    public Orientation DoNotRejoin = Orientation.None;
    public bool AutoPowerOff = false;
    public bool AllowCalibration = true;

    public ControllerConfig(ILogger? logger) : base(logger) { }

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
        UseFilteredMotion = config.UseFilteredMotion;
        DebugType = config.DebugType;
        DoNotRejoin = config.DoNotRejoin;
        AutoPowerOff = config.AutoPowerOff;
        AllowCalibration = config.AllowCalibration;
    }

    public override void Update()
    {
        TryUpdateSetting("LowFreqRumble", ref LowFreq);
        TryUpdateSetting("HighFreqRumble", ref HighFreq);
        TryUpdateSetting("EnableRumble", ref EnableRumble);
        TryUpdateSetting("ShowAsXInput", ref ShowAsXInput);
        TryUpdateSetting("ShowAsDS4", ref ShowAsDs4);
        TryUpdateSetting("StickLeftDeadzone", ref StickLeftDeadzone);
        TryUpdateSetting("StickRightDeadzone", ref StickRightDeadzone);
        TryUpdateSetting("StickLeftRange", ref StickLeftRange);
        TryUpdateSetting("StickRightRange", ref StickRightRange);
        TryUpdateSetting("SticksSquared", ref SticksSquared);
        TryUpdateSetting("StickLeftAntiDeadzone", ref StickLeftAntiDeadzone);
        TryUpdateSetting("StickRightAntiDeadzone", ref StickRightAntiDeadzone);
        TryUpdateSetting("AHRS_beta", ref AHRSBeta);
        TryUpdateSetting("ShakeInputDelay", ref ShakeDelay);
        TryUpdateSetting("EnableShakeInput", ref ShakeInputEnabled);
        TryUpdateSetting("ShakeInputSensitivity", ref ShakeSensitivity);
        TryUpdateSetting("ChangeOrientationDoubleClick", ref ChangeOrientationDoubleClick);
        TryUpdateSetting("DragToggle", ref DragToggle);
        TryUpdateSetting("GyroToJoyOrMouse", ref ExtraGyroFeature);
        TryUpdateSetting("GyroAnalogSensitivity", ref GyroAnalogSensitivity);
        TryUpdateSetting("GyroAnalogSliders", ref GyroAnalogSliders);
        TryUpdateSetting("GyroHoldToggle", ref GyroHoldToggle);
        TryUpdateSetting("GyroLeftHanded", ref GyroLeftHanded);
        TryUpdateSetting("GyroMouseSensitivity", ref GyroMouseSensitivity);
        TryUpdateSetting("GyroStickReduction", ref GyroStickReduction);
        TryUpdateSetting("GyroStickSensitivity", ref GyroStickSensitivity);
        TryUpdateSetting("HomeLongPowerOff", ref HomeLongPowerOff);
        TryUpdateSetting("HomeLEDOn", ref HomeLEDOn);
        TryUpdateSetting("PowerOffInactivity", ref PowerOffInactivityMins);
        TryUpdateSetting("SwapAB", ref SwapAB);
        TryUpdateSetting("SwapXY", ref SwapXY);
        TryUpdateSetting("MinusToShare", ref MinusToShare);
        TryUpdateSetting("UseFilteredIMU", ref UseFilteredMotion);
        TryUpdateSetting("DebugType", ref DebugType);
        TryUpdateSetting("DoNotRejoinJoycons", ref DoNotRejoin);
        TryUpdateSetting("AutoPowerOff", ref AutoPowerOff);
        TryUpdateSetting("AllowCalibration", ref AllowCalibration);
    }



    public override ControllerConfig Clone()
    {
        return new ControllerConfig(this);
    }
}
