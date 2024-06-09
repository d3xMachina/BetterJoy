using System;
using System.ComponentModel;

namespace BetterJoy.Exceptions;

public static class ExceptionExtensions
{
    public static string Display(this Exception e, bool stackTrace = false)
    {
        var message = "(";

        switch (e)
        {
            case Win32Exception win32Ex:
                message += $"0x{win32Ex.NativeErrorCode:X} - {win32Ex.Message}";
                break;
            case DeviceNullHandleException:
            case DeviceComFailedException:
            case DeviceQueryFailedException:
                message += $"{e.Message}";
                break;
            default:
                message += $"{e.GetType()} - {e.Message}";
                break;
        }

        if (stackTrace)
        {
            message += $" - {e.StackTrace}";
        }

        message += ")";

        return message;
    }
}