using BetterJoy.HIDApi.Native;
using System;

namespace BetterJoy.HIDApi;

public sealed class DeviceNotificationEventArgs : EventArgs
{
    public DeviceNotificationEventArgs(DeviceInfo deviceInfo, HotplugEvent ev)
    {
        DeviceInfo = deviceInfo;
        DeviceEvent = ev;
    }

    public DeviceInfo DeviceInfo { get; }

    public HotplugEvent DeviceEvent { get; }
}