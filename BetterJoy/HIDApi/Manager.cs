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
        return NativeMethods.HotplugRegisterCallback(
            vendorId,
            productId,
            events,
            flags,
            callback,
            userData,
            out callbackHandle
        );
    }

    public static int HotplugDeregisterCallback(int callbackHandle)
    {
        return NativeMethods.HotplugDeregisterCallback(callbackHandle);
    }
}