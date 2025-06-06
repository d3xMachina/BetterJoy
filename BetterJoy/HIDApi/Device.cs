using System;
using System.Runtime.InteropServices;

namespace BetterJoy.HIDApi;

public sealed class Device : IDisposable
{
    private IntPtr _deviceHandle;
    private bool _disposed;

    public bool IsValid => _deviceHandle != IntPtr.Zero;

    private Device(IntPtr deviceHandle)
    {
        _deviceHandle = deviceHandle;
    }

    public static Device Open(ushort vendorId, ushort productId, string serialNumber)
    {
        return new Device(Native.NativeMethods.Open(vendorId, productId, serialNumber));
    }

    public static Device OpenPath(string path)
    {
        return new Device(Native.NativeMethods.OpenPath(path));
    }

    public int Write(ReadOnlySpan<byte> data, int length)
    {
        return Native.NativeMethods.Write(_deviceHandle, ref MemoryMarshal.GetReference(data), (nuint)length);
    }

    public int ReadTimeout(Span<byte> data, int length, int milliseconds)
    {
        return Native.NativeMethods.ReadTimeout(_deviceHandle, ref MemoryMarshal.GetReference(data), (nuint)length, milliseconds);
    }

    public int Read(Span<byte> data, int length)
    {
        return Native.NativeMethods.Read(_deviceHandle, ref MemoryMarshal.GetReference(data), (nuint)length);
    }

    public int SetNonBlocking(int nonblock)
    {
        return Native.NativeMethods.SetNonBlocking(_deviceHandle, nonblock);
    }

    public int SendFeatureReport(ReadOnlySpan<byte> data, int length)
    {
        return Native.NativeMethods.SendFeatureReport(_deviceHandle, ref MemoryMarshal.GetReference(data), (nuint)length);
    }

    public int GetFeatureReport(Span<byte> data, int length)
    {
        return Native.NativeMethods.GetFeatureReport(_deviceHandle, ref MemoryMarshal.GetReference(data), (nuint)length);
    }

    public string GetManufacturer()
    {
        return StringUtils.GetUnicodeString(
            (buffer, length) => Native.NativeMethods.GetManufacturerString(_deviceHandle, ref MemoryMarshal.GetReference(buffer), length)
        );
    }

    public string GetProduct()
    {
        return StringUtils.GetUnicodeString(
            (buffer, length) => Native.NativeMethods.GetProductString(_deviceHandle, ref MemoryMarshal.GetReference(buffer), length)
        );
    }

    public string GetSerialNumber()
    {
        return StringUtils.GetUnicodeString(
            (buffer, length) => Native.NativeMethods.GetSerialNumberString(_deviceHandle, ref MemoryMarshal.GetReference(buffer), length)
        );
    }

    public string GetIndexed(int stringIndex)
    {
        return StringUtils.GetUnicodeString(
            (buffer, length) => Native.NativeMethods.GetIndexedString(_deviceHandle, stringIndex, ref MemoryMarshal.GetReference(buffer), length)
        );
    }

    public string GetInstance()
    {
        return StringUtils.GetUnicodeString(
            (buffer, length) => Native.NativeMethods.GetInstanceString(_deviceHandle, ref MemoryMarshal.GetReference(buffer), length)
        );
    }

    public string GetParentInstance()
    {
        return StringUtils.GetUnicodeString(
            (buffer, length) => Native.NativeMethods.GetParentInstanceString(_deviceHandle, ref MemoryMarshal.GetReference(buffer), length)
        );
    }

    public int GetContainerId(out Guid containerId)
    {
        return Native.NativeMethods.GetContainerId(_deviceHandle, out containerId);
    }

    public string GetError()
    {
        var ptr = Native.NativeMethods.Error(_deviceHandle);
        return Marshal.PtrToStringUni(ptr);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_deviceHandle != IntPtr.Zero)
        {
            Native.NativeMethods.Close(_deviceHandle);
            _deviceHandle = IntPtr.Zero;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~Device()
    {
        Dispose();
    }
}
