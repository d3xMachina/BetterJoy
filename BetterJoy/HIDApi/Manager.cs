using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BetterJoy.HIDApi;

public static class Manager
{
    public static int Init()
    {
        return NativeMethods.Init();
    }

    public static int Exit()
    {
        return NativeMethods.Exit();
    }

    public static string GetError()
    {
        var ptr = NativeMethods.Error(IntPtr.Zero);
        return Marshal.PtrToStringUni(ptr);
    }

    public static IEnumerable<DeviceInfo> EnumerateDevices(ushort vendorId, ushort productId)
    {
        var devicesPtr = NativeMethods.Enumerate(vendorId, productId);
        if (devicesPtr == IntPtr.Zero)
        {
            yield break;
        }

        try
        {
            DeviceInfo? currentDevice = Marshal.PtrToStructure<DeviceInfo>(devicesPtr);

            do
            {
                yield return currentDevice.Value;
                currentDevice = currentDevice.Value.Next;
            }
            while (currentDevice != null);
        }
        finally
        {
            NativeMethods.FreeEnumeration(devicesPtr);
        }
    }

    public static int HotplugRegisterCallback(
        ushort vendorId,
        ushort productId,
        int events,
        int flags,
        HotplugCallback callback,
        object userData,
        out int callbackHandle)
    {
        // Create the native callback that will forward to our managed callback
        int nativeCallback(int handle, DeviceInfo deviceInfo, int ev, nint _)
        {
            return callback(handle, deviceInfo, ev, userData);
        }

        // Register the native callback
        var result = NativeMethods.HotplugRegisterCallback(
            vendorId,
            productId,
            events,
            flags,
            nativeCallback,
            IntPtr.Zero, // We don't pass userData through native
            out callbackHandle
        );

        return result;
    }

    public static int HotplugDeregisterCallback(int callbackHandle)
    {
        return NativeMethods.HotplugDeregisterCallback(callbackHandle);
    }
}