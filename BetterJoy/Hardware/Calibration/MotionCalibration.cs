using System;

namespace BetterJoy.Hardware.Calibration;

public class MotionCalibration
{
    public record ThreeAxisShort(short X, short Y, short Z)
    {
        public override string ToString()
        {
            return $"X: {X}, Y: {Y}, Z: {Z}";
        }
        
        public bool Invalid => X == -1 || Y == -1 || Z == -1;
    }
    
    private readonly ThreeAxisShort _defaultAccelerometerNeutralConfig =     new(    0,     0,     0);
    private readonly ThreeAxisShort _defaultAccelerometerSensitivityConfig = new(16384, 16384, 16384);
    private readonly ThreeAxisShort _defaultGyroscopeNeutralConfig =         new(    0,     0,     0);
    private readonly ThreeAxisShort _defaultGyroscopeSensitivityConfig =     new(13371, 13371, 13371);

    public ThreeAxisShort AccelerometerNeutral { get; private set; }
    public ThreeAxisShort AccelerometerSensitivity { get; private set; }
    public ThreeAxisShort GyroscopeNeutral { get; private set; }
    public ThreeAxisShort GyroscopeSensitivity { get; private set; }
    public bool UsedDefaultValues { get; private set; }

    public MotionCalibration()
    {
        AccelerometerNeutral = _defaultAccelerometerNeutralConfig;
        AccelerometerSensitivity = _defaultAccelerometerSensitivityConfig;
        GyroscopeNeutral = _defaultGyroscopeNeutralConfig;
        GyroscopeSensitivity = _defaultGyroscopeSensitivityConfig;
        UsedDefaultValues = true;
    }
    
    public MotionCalibration(ReadOnlySpan<short> values)
    {
        InitFromValues(values);
    }
    
    public MotionCalibration(ReadOnlySpan<byte> raw)
    {
        InitFromBytes(raw);
    }
    
    private void InitFromBytes(ReadOnlySpan<byte> raw)
    {
        if (raw.Length != 24)
        {
            throw new ArgumentException($"{nameof(StickRangeCalibration)} expects 24 bytes.");
        }

        InitFromValues([
            BitWrangler.EncodeBytesAsWordLittleEndianSigned(raw[0],  raw[1]),
            BitWrangler.EncodeBytesAsWordLittleEndianSigned(raw[2],  raw[3]),
            BitWrangler.EncodeBytesAsWordLittleEndianSigned(raw[4],  raw[5]),
            BitWrangler.EncodeBytesAsWordLittleEndianSigned(raw[6],  raw[7]),
            BitWrangler.EncodeBytesAsWordLittleEndianSigned(raw[8],  raw[9]),
            BitWrangler.EncodeBytesAsWordLittleEndianSigned(raw[10], raw[11]),
            BitWrangler.EncodeBytesAsWordLittleEndianSigned(raw[12], raw[13]),
            BitWrangler.EncodeBytesAsWordLittleEndianSigned(raw[14], raw[15]),
            BitWrangler.EncodeBytesAsWordLittleEndianSigned(raw[16], raw[17]),
            BitWrangler.EncodeBytesAsWordLittleEndianSigned(raw[18], raw[19]),
            BitWrangler.EncodeBytesAsWordLittleEndianSigned(raw[20], raw[21]),
            BitWrangler.EncodeBytesAsWordLittleEndianSigned(raw[22], raw[23]),
        ]);
    }
    

    private void InitFromValues(ReadOnlySpan<short> values)
    {
        if (values.Length != 12)
        {
            throw new ArgumentException($"{nameof(StickRangeCalibration)} expects 12 values");
        }

        var inputAccelerometerNeutral     = new ThreeAxisShort(values[0], values[1],  values[2]);
        var inputAccelerometerSensitivity = new ThreeAxisShort(values[3], values[4],  values[5]);
        var inputGyroscopeNeutral         = new ThreeAxisShort(values[6], values[7],  values[8]);
        var inputGyroscopeSensitivity     = new ThreeAxisShort(values[9], values[10], values[11]);
        

        AccelerometerNeutral = inputAccelerometerNeutral.Invalid
            ? _defaultAccelerometerNeutralConfig 
            : inputAccelerometerNeutral;

        AccelerometerSensitivity = inputAccelerometerSensitivity.Invalid
            ? _defaultAccelerometerSensitivityConfig
            : inputAccelerometerSensitivity;
        
        GyroscopeNeutral = inputGyroscopeNeutral.Invalid
            ? _defaultGyroscopeNeutralConfig
            : inputGyroscopeNeutral;

        GyroscopeSensitivity = inputGyroscopeSensitivity.Invalid
                ? _defaultGyroscopeSensitivityConfig
                : inputGyroscopeSensitivity;
        
        UsedDefaultValues = 
            inputAccelerometerNeutral.Invalid || 
            inputAccelerometerSensitivity.Invalid || 
            inputGyroscopeNeutral.Invalid ||
            inputGyroscopeSensitivity.Invalid;
    }

    public override string ToString()
    {
        return $"Motion calibration data: " +
               $"{nameof(AccelerometerNeutral)}: {AccelerometerNeutral}, " +
               $"{nameof(AccelerometerSensitivity)}: {AccelerometerSensitivity}, " +
               $"{nameof(GyroscopeNeutral)}: {GyroscopeNeutral}, " +
               $"{nameof(GyroscopeSensitivity)} {GyroscopeSensitivity}";
    }
}
