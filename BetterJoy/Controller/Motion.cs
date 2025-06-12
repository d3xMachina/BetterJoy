using System.Numerics;

namespace BetterJoy.Controller;

public struct Motion(Vector3 Gyroscope, Vector3 Accelerometer)
{
    public Vector3 Gyroscope = Gyroscope;
    public Vector3 Accelerometer = Accelerometer;
}
