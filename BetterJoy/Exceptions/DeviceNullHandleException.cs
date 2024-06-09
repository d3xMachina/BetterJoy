using System;

namespace BetterJoy.Exceptions;

public class DeviceNullHandleException : Exception
{
    public DeviceNullHandleException() { }

    public DeviceNullHandleException(string message) : base(message) { }

    public DeviceNullHandleException(string message, Exception innerException) : base(message, innerException) { }
}