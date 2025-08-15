using System.Numerics;

namespace BetterJoy.Controller;

public struct Motion(Vector3 gyroscope, Vector3 accelerometer)
{
    public Vector3 Gyroscope = gyroscope;
    public Vector3 Accelerometer = accelerometer;
}
