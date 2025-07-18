using BetterJoy.Hardware.Data;
using System;

namespace BetterJoy.Hardware.Calibration;

public class StickLimitsCalibration
{
    private static readonly ushort[] _defaultCalibration = [2048, 2048, 2048, 2048, 2048, 2048]; // Default stick calibration
    public ushort XMax { get; private set; }
    public ushort YMax { get; private set; }
    public ushort XCenter { get; private set; }
    public ushort YCenter { get; private set; }
    public ushort XMin { get; private set; }
    public ushort YMin { get; private set; }
    private bool? _isLeft;

    public StickLimitsCalibration(bool? isLeft = null)
    {
        InitFromValues(_defaultCalibration, isLeft);
    }

    public StickLimitsCalibration(ReadOnlySpan<ushort> values, bool? isLeft = null)
    {
        InitFromValues(values, isLeft);
    }

    public static StickLimitsCalibration FromRightStickCalibrationBytes(ReadOnlySpan<byte> raw)
    {
        return new StickLimitsCalibration(raw, false);
    }

    public static StickLimitsCalibration FromLeftStickCalibrationBytes(ReadOnlySpan<byte> raw)
    {
        return new StickLimitsCalibration(raw, true);
    }

    private StickLimitsCalibration(ReadOnlySpan<byte> raw, bool isLeft)
    {
        InitFromBytes(raw, isLeft);
    }

    private void InitFromBytes(ReadOnlySpan<byte> raw, bool isLeft)
    {
        if (raw.Length != 9)
        {
            throw new ArgumentException($"{nameof(StickLimitsCalibration)} expects 9 bytes, got {raw.Length}.");
        }

        int offset = isLeft ? 0 : 6;

        InitFromValues([
            BitWrangler.Lower3NibblesLittleEndian(raw[IndexOffsetter(0, offset)], raw[IndexOffsetter(1, offset)]),
            BitWrangler.Upper3NibblesLittleEndian(raw[IndexOffsetter(1, offset)], raw[IndexOffsetter(2, offset)]),
            BitWrangler.Lower3NibblesLittleEndian(raw[IndexOffsetter(3, offset)], raw[IndexOffsetter(4, offset)]),
            BitWrangler.Upper3NibblesLittleEndian(raw[IndexOffsetter(4, offset)], raw[IndexOffsetter(5, offset)]),
            BitWrangler.Lower3NibblesLittleEndian(raw[IndexOffsetter(6, offset)], raw[IndexOffsetter(7, offset)]),
            BitWrangler.Upper3NibblesLittleEndian(raw[IndexOffsetter(7, offset)], raw[IndexOffsetter(8, offset)]),
        ], isLeft);
    }

    private static int IndexOffsetter(int index, int offset)
    {
        return (index + offset) % 9;
    }

    private void InitFromValues(ReadOnlySpan<ushort> values, bool? isLeft)
    {
        if (values.Length != 6)
        {
            throw new ArgumentException($"{nameof(StickLimitsCalibration)} expects 6 values, got {values.Length}.");
        }

#pragma warning disable IDE0055 // Disable formatting
        _isLeft = isLeft;
        XMax    = values[0];
        YMax    = values[1];
        XCenter = values[2];
        YCenter = values[3];
        XMin    = values[4];
        YMin    = values[5];
#pragma warning restore IDE0055
    }

    public override string ToString()
    {
        string name = _isLeft == null ? "S" : _isLeft.Value ? "Left s" : "Right s";
        return $"{name}tick calibration data: (XMin: {XMin:D}, XCenter: {XCenter:D}, XMax: {XMax:D}, YMin: {YMin:D}, YCenter: {YCenter:D}, YMax: {YMax:D})";
    }
}
