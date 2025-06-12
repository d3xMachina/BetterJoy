namespace BetterJoy.Hardware.Data;

public struct MotionShort(ThreeAxisShort Gyroscope, ThreeAxisShort Accelerometer)
{
    public ThreeAxisShort Gyroscope = Gyroscope;
    public ThreeAxisShort Accelerometer = Accelerometer;
}
