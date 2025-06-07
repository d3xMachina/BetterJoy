using System;

namespace BetterJoy.Hardware.Calibration;

public class RightStickRangeCalibration : StickRangeCalibration
{
    public RightStickRangeCalibration() { }
    public RightStickRangeCalibration(ReadOnlySpan<byte> raw) : base(raw, 2) { }
    public RightStickRangeCalibration(ReadOnlySpan<ushort> values) : base(values) { }
    
    public override string ToString() => ToString(nameof(RightStickRangeCalibration));
}
