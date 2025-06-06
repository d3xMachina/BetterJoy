using BetterJoy.HIDApi.Exceptions;
using BetterJoy.HIDApi.Native;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BetterJoy.HIDApi;

public delegate void DeviceNotificationReceivedEventHandler(object sender, DeviceNotificationEventArgs e);

public static class Manager
{
    public static event DeviceNotificationReceivedEventHandler DeviceNotificationReceived;

    private static int _deviceNotificationsHandle = 0; // A valid callback handle is a positive integer

    public static void Init()
    {
        int ret = Native.NativeMethods.Init();
        if (ret != 0)
        {
            throw new HIDApiInitFailedException(GetError());
        }
    }

    // It also deregister all callbacks
    public static void Exit()
    {
        // Ignore if there is a returned error, we can't do anything about it
        _ = Native.NativeMethods.Exit();
    }

    public static string GetError()
    {
        var ptr = Native.NativeMethods.Error(IntPtr.Zero);
        return Marshal.PtrToStringUni(ptr);
    }

    public static IEnumerable<DeviceInfo> EnumerateDevices(ushort vendorId, ushort productId)
    {
        var devicesPtr = Native.NativeMethods.Enumerate(vendorId, productId);
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
            Native.NativeMethods.FreeEnumeration(devicesPtr);
        }
    }

    public static void StartDeviceNotifications()
    {
        if (_deviceNotificationsHandle != 0)
        {
            return;
        }

        static int notificationCallback(int callbackHandle, DeviceInfo deviceInfo, int events, IntPtr userData)
        {
            DeviceNotificationReceived?.Invoke(null, new DeviceNotificationEventArgs(deviceInfo, (HotplugEvent)events));
            return 0; // keep the callback registered
        };

        int ret = Native.NativeMethods.HotplugRegisterCallback(
            0x0,
            0x0,
            (int)(HotplugEvent.DeviceArrived | HotplugEvent.DeviceLeft),
            (int)HotplugFlag.Enumerate,
            notificationCallback,
            IntPtr.Zero,
            out _deviceNotificationsHandle
        );

        if (ret != 0)
        {
            throw new HIDApiCallbackFailedException();
        }
    }

    public static void StopDeviceNotifications()
    {
        if (_deviceNotificationsHandle == 0)
        {
            return;
        }

        int ret = Native.NativeMethods.HotplugDeregisterCallback(_deviceNotificationsHandle);
        if (ret != 0)
        {
            throw new HIDApiCallbackFailedException();
        }

        _deviceNotificationsHandle = 0;
    }
}
