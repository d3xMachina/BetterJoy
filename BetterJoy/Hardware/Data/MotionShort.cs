namespace BetterJoy.Hardware.Data;

public struct MotionShort(ThreeAxisShort gyroscope, ThreeAxisShort accelerometer)
{
    public ThreeAxisShort Gyroscope = gyroscope;
    public ThreeAxisShort Accelerometer = accelerometer;
}
