using System;

namespace BetterJoy.Exceptions;

public class DeviceComFailedException : Exception
{
    public DeviceComFailedException() { }

    public DeviceComFailedException(string message) : base(message) { }

    public DeviceComFailedException(string message, Exception innerException) : base(message, innerException) { }
}
