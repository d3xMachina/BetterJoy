using System;

namespace BetterJoy.Hardware.Calibration;

public class RightStickRangeCalibration : StickRangeCalibration
{
    public RightStickRangeCalibration() { }
    public RightStickRangeCalibration(ReadOnlySpan<byte> raw) : base(raw, 2) { }
    public RightStickRangeCalibration(ReadOnlySpan<ushort> values) : base(values) { }
    
    public override string ToString()
    {
        return $"{nameof(RightStickRangeCalibration)} data: (XMax: {XMax:S}, YMax: {YMax:S}, XCenter: {XCenter:S}, YCenter: {YCenter:S}, XMin: {XMin:S}, YMin: {YMin:S})";
    }
}
