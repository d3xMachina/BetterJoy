using System;

namespace BetterJoy.HIDApi.Exceptions;

public class HIDApiInitFailedException : Exception
{
    public HIDApiInitFailedException() { }

    public HIDApiInitFailedException(string message) : base(message) { }

    public HIDApiInitFailedException(string message, Exception innerException) : base(message, innerException) { }
}
