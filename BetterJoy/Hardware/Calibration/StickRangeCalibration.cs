using BetterJoy.Config;
using BetterJoy.Hardware.Data;
using System;

namespace BetterJoy.Hardware.Calibration;

public readonly struct StickRangeCalibration
{
    private readonly float _value;

    public StickRangeCalibration()
    {
        _value = 0;
    }

    public StickRangeCalibration(float value)
    {
        _value = value;
    }

    public StickRangeCalibration(Span<byte> raw)
    {
        if (raw.Length != 2)
        {
            throw new ArgumentException($"{nameof(StickRangeCalibration)} expects 2 bytes, got {raw.Length}.");
        }

        _value = CalculateRange(BitWrangler.Upper3NibblesLittleEndian(raw[0], raw[1]));
    }

    public static StickRangeCalibration FromConfigRight(ControllerConfig config)
    {
        return new StickRangeCalibration(config.StickRightRange);
    }

    public static StickRangeCalibration FromConfigLeft(ControllerConfig config)
    {
        return new StickRangeCalibration(config.StickLeftRange);
    }

    public static implicit operator float(StickRangeCalibration range) => range._value;

    private static float CalculateRange(ushort value)
    {
        return (float)value / 0xFFF;
    }
}
