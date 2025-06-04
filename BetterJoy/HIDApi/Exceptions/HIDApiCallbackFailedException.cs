using System;

namespace BetterJoy.HIDApi.Exceptions;

public class HIDApiCallbackFailedException : Exception
{
    public HIDApiCallbackFailedException() { }

    public HIDApiCallbackFailedException(string message) : base(message) { }

    public HIDApiCallbackFailedException(string message, Exception innerException) : base(message, innerException) { }
}
