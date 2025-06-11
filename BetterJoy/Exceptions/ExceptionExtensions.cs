using Nefarius.ViGEm.Client.Exceptions;
using System;
using System.ComponentModel;
using System.Configuration;

namespace BetterJoy.Exceptions;

public static class ExceptionExtensions
{
    public static string Display(this Exception e, bool stackTrace = false)
    {
        var message = "(";

#pragma warning disable IDE0066 // Convert switch statement to expression
        switch (e)
        {
            case Win32Exception win32Ex:
                message += $"0x{win32Ex.NativeErrorCode:X} - {win32Ex.Message}";
                break;
            case BadImageFormatException:
            case ConfigurationErrorsException:
            case DeviceNullHandleException:
            case DeviceComFailedException:
            case DeviceQueryFailedException:
            case VigemBusNotFoundException:
            case VigemBusAccessFailedException:
            case VigemBusVersionMismatchException:
            case VigemAllocFailedException:
            case VigemAlreadyConnectedException:
                message += $"{e.Message}";
                break;
            default:
                message += $"{e.GetType()} - {e.Message}";
                break;
        }
#pragma warning restore IDE0066

        message += ")";

        if (stackTrace)
        {
            message += $"{Environment.NewLine}{e.StackTrace}";
        }

        return message;
    }
}
