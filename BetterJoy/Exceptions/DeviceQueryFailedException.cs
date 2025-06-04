using System;

namespace BetterJoy.Exceptions;

public class DeviceQueryFailedException : Exception
{
    public DeviceQueryFailedException() { }

    public DeviceQueryFailedException(string message) : base(message) { }

    public DeviceQueryFailedException(string message, Exception innerException) : base(message, innerException) { }
}
