using System;
using System.Runtime.InteropServices;
using System.Text;

namespace BetterJoy
{
    public static class HIDApi
    {
        private const string Dll = "hidapi.dll";
        private const int MaxStringLength = 255;

        public enum BusType
        {
            Unknown = 0x00,
            USB = 0x01,
            Bluetooth = 0x02,
            I2C = 0x03,
            SPI = 0x04
        }
        
        // Added in the official hidapi callback branch
        #region HIDAPI_CALLBACK

        public struct HIDDeviceInfo
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
            public IntPtr Next;
            public BusType BusType; // >= 0.13.0
        }

        [Flags]
        public enum HotplugEvent
        {
            DeviceArrived = (1 << 0),
            DeviceLeft = (1 << 1)
        }

        [Flags]
        public enum HotplugFlag
        {
            None = 0,
            Enumerate = (1 << 0)
        }

        public delegate int HotPlugCallbackFunction(
            int callbackHandle,
            [MarshalAs(UnmanagedType.Struct)] HIDDeviceInfo deviceInfo,
            int events,
            [MarshalAs(UnmanagedType.IUnknown)] object userData
        );

        [DllImport(Dll, EntryPoint = "hid_hotplug_register_callback", CallingConvention = CallingConvention.Cdecl)]
        public static extern int HotplugRegisterCallback(
            ushort vendorId,
            ushort productId,
            int events,
            int flags,
            [MarshalAs(UnmanagedType.FunctionPtr)] HotPlugCallbackFunction callback,
            [MarshalAs(UnmanagedType.IUnknown)] object userData,
            out int callbackHandle
        );

        [DllImport(Dll, EntryPoint = "hid_hotplug_deregister_callback", CallingConvention = CallingConvention.Cdecl)]
        public static extern int HotplugDeregisterCallback(int callbackHandle);
        
        #endregion

        [DllImport(Dll, EntryPoint = "hid_init", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Init();

        [DllImport(Dll, EntryPoint = "hid_exit", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Exit();

        [DllImport(Dll, EntryPoint = "hid_enumerate", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr Enumerate(ushort vendorId, ushort productId);

        [DllImport(Dll, EntryPoint = "hid_free_enumeration", CallingConvention = CallingConvention.Cdecl)]
        public static extern void FreeEnumeration(IntPtr phidDeviceInfo);

        [DllImport(Dll, EntryPoint = "hid_open", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr Open(
            ushort vendorId,
            ushort productId,
            [MarshalAs(UnmanagedType.LPWStr)] string serialNumber
        );

        [DllImport(Dll, EntryPoint = "hid_open_path", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr OpenPath([MarshalAs(UnmanagedType.LPStr)] string path);

        [DllImport(Dll, EntryPoint = "hid_write", CallingConvention = CallingConvention.Cdecl)]
        private static extern int Write(IntPtr device, ref byte data, UIntPtr length);

        [DllImport(Dll, EntryPoint = "hid_read_timeout", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ReadTimeout(IntPtr dev, ref byte data, UIntPtr length, int milliseconds);

        [DllImport(Dll, EntryPoint = "hid_read", CallingConvention = CallingConvention.Cdecl)]
        private static extern int Read(IntPtr device, ref byte data, UIntPtr length);

        [DllImport(Dll, EntryPoint = "hid_set_nonblocking", CallingConvention = CallingConvention.Cdecl)]
        public static extern int SetNonBlocking(IntPtr device, int nonblock);

        [DllImport(Dll, EntryPoint = "hid_send_feature_report", CallingConvention = CallingConvention.Cdecl)]
        public static extern int SendFeatureReport(IntPtr device, ref byte data, UIntPtr length);

        [DllImport(Dll, EntryPoint = "hid_get_feature_report", CallingConvention = CallingConvention.Cdecl)]
        public static extern int GetFeatureReport(IntPtr device, ref byte data, UIntPtr length);

        [DllImport(Dll, EntryPoint = "hid_close", CallingConvention = CallingConvention.Cdecl)]
        public static extern void Close(IntPtr device);

        [DllImport(Dll, EntryPoint = "hid_get_manufacturer_string", CallingConvention = CallingConvention.Cdecl)]
        private static extern int GetManufacturerString(
            IntPtr device,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder str,
            UIntPtr maxlen
        );

        [DllImport(Dll, EntryPoint = "hid_get_product_string", CallingConvention = CallingConvention.Cdecl)]
        private static extern int GetProductString(
            IntPtr device,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder str,
            UIntPtr maxlen
        );

        [DllImport(Dll, EntryPoint = "hid_get_serial_number_string", CallingConvention = CallingConvention.Cdecl)]
        private static extern int GetSerialNumberString(
            IntPtr device,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder str,
            UIntPtr maxlen
        );

        [DllImport(Dll, EntryPoint = "hid_get_indexed_string", CallingConvention = CallingConvention.Cdecl)]
        private static extern int GetIndexedString(
            IntPtr device,
            int stringIndex,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder str,
            UIntPtr maxlen
        );

        [DllImport(Dll, EntryPoint = "hid_error", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ErrorPtr(IntPtr device);

        [DllImport(Dll, EntryPoint = "hid_winapi_get_container_id", CallingConvention = CallingConvention.Cdecl)]
        public static extern int GetContainerId(IntPtr device, out Guid containerId);

        private static string GetStringHelper(Func<StringBuilder, int> getStringFunc)
        {
            var bufferInstance = new StringBuilder(MaxStringLength);
            var ret = getStringFunc(bufferInstance);

            if (ret < 0)
            {
                return string.Empty;
            }

            return bufferInstance.ToString();
        }

        // Added in my fork of HIDapi at https://github.com/d3xMachina/hidapi (needed for HIDHide to work correctly)
        #region HIDAPI_MYFORK

        [DllImport(Dll, EntryPoint = "hid_winapi_get_instance_string", CallingConvention = CallingConvention.Cdecl)]
        private static extern int GetInstanceString(
            IntPtr device,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder str,
            UIntPtr maxlen
        );

        [DllImport(Dll, EntryPoint = "hid_winapi_get_parent_instance_string", CallingConvention = CallingConvention.Cdecl)]
        private static extern int GetParentInstanceString(
            IntPtr device,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder str,
            UIntPtr maxlen
        );

        public static string GetInstance(IntPtr device)
        {
            return GetStringHelper(bufferInstance => GetInstanceString(device, bufferInstance, MaxStringLength));
        }

        public static string GetParentInstance(IntPtr device)
        {
            return GetStringHelper(bufferInstance => GetParentInstanceString(device, bufferInstance, MaxStringLength));
        }

        #endregion

        public static string Error(IntPtr device)
        {
            var ptr = ErrorPtr(device);
            return Marshal.PtrToStringUni(ptr);
        }

        public static int Write(IntPtr device, ReadOnlySpan<byte> data, int length)
        {
            return Write(device, ref MemoryMarshal.GetReference(data), (nuint)length);
        }

        public static int ReadTimeout(IntPtr device, Span<byte> data, int length, int milliseconds)
        {
            return ReadTimeout(device, ref MemoryMarshal.GetReference(data), (nuint)length, milliseconds);
        }

        public static int Read(IntPtr device, Span<byte> data, int length)
        {
            return Read(device, ref MemoryMarshal.GetReference(data), (nuint)length);
        }

        public static string GetManufacturer(IntPtr device)
        {
            return GetStringHelper(bufferInstance => GetManufacturerString(device, bufferInstance, MaxStringLength));
        }

        public static string GetProduct(IntPtr device)
        {
            return GetStringHelper(bufferInstance => GetProductString(device, bufferInstance, MaxStringLength));
        }

        public static string GetSerialNumber(IntPtr device)
        {
            return GetStringHelper(bufferInstance => GetSerialNumberString(device, bufferInstance, MaxStringLength));
        }

        public static string GetIndexed(IntPtr device, int stringIndex)
        {
            return GetStringHelper(bufferInstance => GetIndexedString(device, stringIndex, bufferInstance, MaxStringLength));
        }
    }
}
