using System;
using System.Runtime.InteropServices;

namespace BetterJoy.HIDApi.Native;

[Flags]
public enum HotplugEvent
{
    DeviceArrived = 1 << 0,
    DeviceLeft = 1 << 1
}

[Flags]
public enum HotplugFlag
{
    None = 0,
    Enumerate = 1 << 0
}

public delegate int HotplugCallback(
    int callbackHandle,
    [MarshalAs(UnmanagedType.Struct)] DeviceInfo deviceInfo,
    int events,
    IntPtr userData
);
