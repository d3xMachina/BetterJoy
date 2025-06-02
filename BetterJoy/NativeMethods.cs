using System;
using System.Runtime.InteropServices;

namespace BetterJoy;

internal static partial class NativeMethods
{
    // SetDefaultDllDirectories flag parameter
    public const uint LOAD_LIBRARY_SEARCH_APPLICATION_DIR = 0x00000200;
    public const uint LOAD_LIBRARY_SEARCH_USER_DIRS = 0x00000400;
    public const uint LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800;
    public const uint LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetDefaultDllDirectories(uint directoryFlags);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr AddDllDirectory(string directory);
}
