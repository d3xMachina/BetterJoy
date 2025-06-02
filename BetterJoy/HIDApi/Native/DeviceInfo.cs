using System;
using System.Runtime.InteropServices;

namespace BetterJoy.HIDApi;

public enum BusType
{
    Unknown = 0x00,
    USB = 0x01,
    Bluetooth = 0x02,
    I2C = 0x03,
    SPI = 0x04
}

[StructLayout(LayoutKind.Sequential)]
public struct DeviceInfo
{
    [MarshalAs(UnmanagedType.LPStr)] public string Path;
    public ushort VendorId;
    public ushort ProductId;
    [MarshalAs(UnmanagedType.LPWStr)] public string SerialNumber;
    public ushort ReleaseNumber;
    [MarshalAs(UnmanagedType.LPWStr)] public string ManufacturerString;
    [MarshalAs(UnmanagedType.LPWStr)] public string ProductString;
    public ushort UsagePage;
    public ushort Usage;
    public int InterfaceNumber;
    private readonly IntPtr _next;
    public BusType BusType; // >= 0.13.0

    public readonly DeviceInfo? Next
    {
        get
        {
            if (_next == IntPtr.Zero)
            {
                return null;
            }

            return Marshal.PtrToStructure<DeviceInfo>(_next);
        }
    }
}