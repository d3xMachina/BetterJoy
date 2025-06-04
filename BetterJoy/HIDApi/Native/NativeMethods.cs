using System;
using System.Runtime.InteropServices;

namespace BetterJoy.HIDApi;

internal static partial class NativeMethods
{
    private const string Dll = "hidapi.dll";

    [LibraryImport(Dll, EntryPoint = "hid_init")]
    public static partial int Init();

    [LibraryImport(Dll, EntryPoint = "hid_exit")]
    public static partial int Exit();

    [LibraryImport(Dll, EntryPoint = "hid_enumerate")]
    public static partial IntPtr Enumerate(ushort vendorId, ushort productId);

    [LibraryImport(Dll, EntryPoint = "hid_free_enumeration")]
    public static partial void FreeEnumeration(IntPtr phidDeviceInfo);

    [LibraryImport(Dll, EntryPoint = "hid_open")]
    public static partial IntPtr Open(ushort vendorId, ushort productId, [MarshalAs(UnmanagedType.LPWStr)] string serialNumber);

    [LibraryImport(Dll, EntryPoint = "hid_open_path")]
    public static partial IntPtr OpenPath([MarshalAs(UnmanagedType.LPStr)] string path);

    [LibraryImport(Dll, EntryPoint = "hid_write")]
    public static partial int Write(IntPtr device, ref byte data, nuint length);

    [LibraryImport(Dll, EntryPoint = "hid_read_timeout")]
    public static partial int ReadTimeout(IntPtr dev, ref byte data, nuint length, int milliseconds);

    [LibraryImport(Dll, EntryPoint = "hid_read")]
    public static partial int Read(IntPtr device, ref byte data, nuint length);

    [LibraryImport(Dll, EntryPoint = "hid_set_nonblocking")]
    public static partial int SetNonBlocking(IntPtr device, int nonblock);

    [LibraryImport(Dll, EntryPoint = "hid_send_feature_report")]
    public static partial int SendFeatureReport(IntPtr device, ref byte data, nuint length);

    [LibraryImport(Dll, EntryPoint = "hid_get_feature_report")]
    public static partial int GetFeatureReport(IntPtr device, ref byte data, nuint length);

    [LibraryImport(Dll, EntryPoint = "hid_close")]
    public static partial void Close(IntPtr device);

    [LibraryImport(Dll, EntryPoint = "hid_get_manufacturer_string")]
    public static partial int GetManufacturerString(IntPtr device, ref byte str, nuint maxlen);

    [LibraryImport(Dll, EntryPoint = "hid_get_product_string")]
    public static partial int GetProductString(IntPtr device, ref byte str, nuint maxlen);

    [LibraryImport(Dll, EntryPoint = "hid_get_serial_number_string")]
    public static partial int GetSerialNumberString(IntPtr device, ref byte str, nuint maxlen);

    [LibraryImport(Dll, EntryPoint = "hid_get_indexed_string")]
    public static partial int GetIndexedString(IntPtr device, int stringIndex, ref byte str, nuint maxlen);

    [LibraryImport(Dll, EntryPoint = "hid_error")]
    public static partial IntPtr Error(IntPtr device);

    [LibraryImport(Dll, EntryPoint = "hid_winapi_get_container_id")]
    public static partial int GetContainerId(IntPtr device, out Guid containerId);

    // Added in my fork of HIDapi at https://github.com/d3xMachina/hidapi (needed for HIDHide to work correctly)
    #region HIDAPI_MYFORK

    [LibraryImport(Dll, EntryPoint = "hid_winapi_get_instance_string")]
    public static partial int GetInstanceString(IntPtr device, ref byte str, nuint maxlen);

    [LibraryImport(Dll, EntryPoint = "hid_winapi_get_parent_instance_string")]
    public static partial int GetParentInstanceString(IntPtr device, ref byte str, nuint maxlen);

    #endregion

    #region HIDAPI_CALLBACK

    [LibraryImport(Dll, EntryPoint = "hid_hotplug_register_callback")]
    public static partial int HotplugRegisterCallback(
        ushort vendorId,
        ushort productId,
        int events,
        int flags,
        [MarshalAs(UnmanagedType.FunctionPtr)] HotplugCallback callback,
        IntPtr userData,
        out int callbackHandle
    );

    [LibraryImport(Dll, EntryPoint = "hid_hotplug_deregister_callback")]
    public static partial int HotplugDeregisterCallback(int callbackHandle);

    #endregion
}
