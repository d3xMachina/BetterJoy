using BetterJoy.Config;
using BetterJoy.Hardware.Data;
using System;

namespace BetterJoy.Hardware.Calibration;

public struct StickDeadZoneCalibration
{
    private float _value;

    public StickDeadZoneCalibration()
    {
        _value = 0;
    }

    public StickDeadZoneCalibration(float value)
    {
        _value = value;
    }

    public StickDeadZoneCalibration(StickLimitsCalibration stickLimitsCalibration, Span<byte> raw)
    {
        if (raw.Length != 2)
        {
            throw new ArgumentException($"{nameof(StickDeadZoneCalibration)} expects 2 bytes, got {raw.Length}.");
        }

        _value = CalculateDeadZone(stickLimitsCalibration, BitWrangler.Lower3NibblesLittleEndian(raw[0], raw[1]));
    }

    public static StickDeadZoneCalibration FromConfigRight(ControllerConfig config)
    {
        return new StickDeadZoneCalibration(config.StickRightDeadzone);
    }

    public static StickDeadZoneCalibration FromConfigLeft(ControllerConfig config)
    {
        return new StickDeadZoneCalibration(config.StickLeftDeadzone);
    }

    public static implicit operator float(StickDeadZoneCalibration deadZone) => deadZone._value;

    private static float CalculateDeadZone(StickLimitsCalibration stickLimitsCalibration, ushort deadZone)
    {
        return 2.0f * deadZone / Math.Max(stickLimitsCalibration.XMax + stickLimitsCalibration.XMin, stickLimitsCalibration.YMax + stickLimitsCalibration.YMin);
    }
}
