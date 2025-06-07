using System;

namespace BetterJoy.Hardware.Calibration;

public abstract class StickRangeCalibration
{
    private static readonly ushort[] _defaultCalibration = [2048, 2048, 2048, 2048, 2048, 2048]; // Default stick calibration
    public ushort XMax { get; private set; }
    public ushort YMax { get; private set; }
    public ushort XCenter { get; private set; }
    public ushort YCenter { get; private set; }
    public ushort XMin { get; private set; }
    public ushort YMin { get; private set; }
    public bool IsBlank { get; private set; }

    protected StickRangeCalibration()
    {
        InitFromValues(_defaultCalibration);
    }

    protected StickRangeCalibration(ReadOnlySpan<byte> raw, int offset)
    {
        InitFromBytes(raw, offset);
    }

    protected StickRangeCalibration(ReadOnlySpan<ushort> values)
    {
        InitFromValues(values);
    }

    private void InitFromBytes(ReadOnlySpan<byte> raw, int offset) 
    {
        if (raw.Length != 9)
        {
            throw new ArgumentException($"{nameof(StickRangeCalibration)} expects 9 bytes.");
        }
        
        InitFromValues([
            BitWrangler.Lower3NibblesLittleEndian(raw[IndexOffsetter(0, offset)], raw[IndexOffsetter(1, offset)]),
            BitWrangler.Upper3NibblesLittleEndian(raw[IndexOffsetter(1, offset)], raw[IndexOffsetter(2, offset)]),
            BitWrangler.Lower3NibblesLittleEndian(raw[IndexOffsetter(3, offset)], raw[IndexOffsetter(4, offset)]),
            BitWrangler.Upper3NibblesLittleEndian(raw[IndexOffsetter(4, offset)], raw[IndexOffsetter(5, offset)]),
            BitWrangler.Lower3NibblesLittleEndian(raw[IndexOffsetter(6, offset)], raw[IndexOffsetter(7, offset)]),
            BitWrangler.Upper3NibblesLittleEndian(raw[IndexOffsetter(7, offset)], raw[IndexOffsetter(8, offset)]),
        ]);
    }

    private int IndexOffsetter(int index, int offset)
    {
        return (index + offset) % 9;
    }

    private void InitFromValues(ReadOnlySpan<ushort> values) 
    {
        if (values.Length != 6) throw new ArgumentException($"{nameof(StickRangeCalibration)} expects 6 values");
            
        IsBlank = values.IndexOfAnyExcept(BitWrangler.Lower3Nibbles(ushort.MaxValue)) == -1 ||
                  values.IndexOfAnyExcept((ushort) 0) == -1;
        XMax    = values[0];
        YMax    = values[1];
        XCenter = values[2];
        YCenter = values[3];
        XMin    = values[4];
        YMin    = values[5];
    }

    protected string ToString(string name)
    {
        return $"{name} data: (XMax: {XMax:D}, YMax: {YMax:D}, XCenter: {XCenter:D}, YCenter: {YCenter:D}, XMin: {XMin:D}, YMin: {YMin:D})";
    }
}
