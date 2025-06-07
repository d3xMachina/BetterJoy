using System;

namespace BetterJoy.Hardware.Calibration;

public class LeftStickRangeCalibration : StickRangeCalibration
{
    public LeftStickRangeCalibration() { }
    public LeftStickRangeCalibration(ReadOnlySpan<byte> raw) : base(raw, 0) { }
    public LeftStickRangeCalibration(ReadOnlySpan<ushort> values) : base(values) { }

    public override string ToString() => ToString(nameof(LeftStickRangeCalibration));
}
