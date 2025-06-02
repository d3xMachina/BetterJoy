using System;

namespace BetterJoy.HIDApi;

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
    DeviceInfo deviceInfo,
    int events,
    object userData
);

internal delegate int UnmanagedHotplugCallback(
    int callbackHandle,
    DeviceInfo deviceInfo,
    int events,
    IntPtr userData
);