#nullable disable
using BetterJoy.Collections;
using BetterJoy.Config;
using BetterJoy.Controller;
using BetterJoy.Exceptions;
using BetterJoy.Forms;
using BetterJoy.Network.Server;
using Nefarius.Drivers.HidHide;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WindowsInput.Events;
using WindowsInput.Events.Sources;
using static BetterJoy.Forms._3rdPartyControllers;

namespace BetterJoy;

public readonly struct ControllerIdentifier
{
    public readonly string Path;
    public readonly long TimestampCreation;

    public ControllerIdentifier(Joycon controller)
    {
        Path = controller.Path;
        TimestampCreation = controller.TimestampCreation;
    }
}

internal class Program
{
    public static PhysicalAddress BtMac = new([0, 0, 0, 0, 0, 0]);
    public static UdpServer Server;

    public static ViGEmClient EmClient;

    public static JoyconManager Mgr;

    private static MainForm _form;

    public static readonly ConcurrentList<SController> ThirdpartyCons = [];

    public static ProgramConfig Config;

    private static readonly HidHideControlService _hidHideService = new();
    private static readonly HashSet<string> _blockedDeviceInstances = [];

    private static bool _isRunning;
    public static bool IsSuspended { get; private set; }

    private static readonly string _appGuid = "1bf709e9-c133-41df-933a-c9ff3f664c7b"; // randomly-generated
    private static Mutex _mutexInstance;

    private static bool _keyEventRegistered;
    private static bool _mouseEventRegistered;

    private const string _logFilePath = "LogDebug.txt";
    private static Logger _logger;

    public static readonly string ProgramVersion = $"v{Application.ProductVersion.Split('+')[0]}";

    public static void Start()
    {
        Config = new(_logger);
        Config.Update();

        StartHIDHide();

        try
        {
            EmClient = new ViGEmClient(); // Manages emulated XInput
        }
        catch (VigemBusNotFoundException e)
        {
            _logger?.Log("Could not connect to VIGEmBus. Make sure VIGEmBus driver is installed correctly.", e);
        }
        catch (VigemBusAccessFailedException e)
        {
            _logger?.Log("Could not connect to VIGEmBus. VIGEmBus is busy. Try restarting your computer or reinstalling VIGEmBus driver.", e);
        }
        catch (VigemBusVersionMismatchException e)
        {
            _logger?.Log("Could not connect to VIGEmBus. The installed VIGEmBus driver is not compatible. Install a newer version of VIGEmBus driver.", e);
        }
        catch (VigemAllocFailedException e)
        {
            _logger?.Log("Could not connect to VIGEmBus. Allocation failed. Try restarting your computer or reinstalling VIGEmBus driver.", e);
        }
        catch (VigemAlreadyConnectedException e)
        {
            // should not happen
            _logger?.Log("VIGEmBus is already connected.", e);
        }

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            // Get local BT host MAC
            if (nic.NetworkInterfaceType != NetworkInterfaceType.FastEthernetFx &&
                nic.NetworkInterfaceType != NetworkInterfaceType.Wireless80211)
            {
                if (nic.Name.Contains("Bluetooth"))
                {
                    BtMac = nic.GetPhysicalAddress();
                }
            }
        }

        var controllers = GetSavedThirdpartyControllers();
        UpdateThirdpartyControllers(controllers);

        Mgr = new JoyconManager(_logger, _form);
        Server = new UdpServer(_logger, Mgr.Controllers);

        if (!Config.MotionServer)
        {
            _logger?.Log("Motion server is OFF.");
        }
        else
        {
            Server.Start(Config.IP, Config.Port);
        }

        UpdateInputEvents();

        _logger?.Log("All systems go.");
        Mgr.Start();
        _isRunning = true;
    }

    private static void StartHIDHide()
    {
        try
        {
            if (!Config.UseHIDHide)
            {
                return;
            }

            if (!_hidHideService.IsInstalled)
            {
                _logger?.Log("HIDHide is not installed.", Logger.LogLevel.Warning);
                return;
            }

            _hidHideService.IsAppListInverted = false;

            //if (Config.PurgeAffectedDevices)
            //{
            //    hidHideService.ClearBlockedInstancesList();
            //    return;
            //}

            if (Config.PurgeWhitelist)
            {
                _hidHideService.ClearApplicationsList();
            }

            _hidHideService.AddApplicationPath(Environment.ProcessPath);

            _hidHideService.IsActive = true;
        }
        catch (Exception e)
        {
            _logger?.Log("Unable to start HIDHide.", e);
            return;
        }

        _logger?.Log("HIDHide is enabled.");
    }

    public static void AddDeviceToBlocklist(HIDApi.Device device)
    {
        try
        {
            if (!_hidHideService.IsActive)
            {
                return;
            }

            var devices = new List<string>();

            var instance = device.GetInstance();
            if (instance.Length == 0)
            {
                _logger?.Log("Unable to get device instance.", Logger.LogLevel.Error);
            }
            else
            {
                devices.Add(instance);
            }

            var parentInstance = device.GetParentInstance();
            if (parentInstance.Length == 0)
            {
                _logger?.Log("Unable to get device parent instance.", Logger.LogLevel.Error);
            }
            else
            {
                devices.Add(parentInstance);
            }

            if (devices.Count == 0)
            {
                throw new DeviceQueryFailedException("hidapi error");
            }

            BlockDeviceInstances(devices);
        }
        catch (Exception e)
        {
            _logger?.Log("Unable to add controller to block-list.", e);
        }
    }

    public static void BlockDeviceInstances(IList<string> instances)
    {
        foreach (var instance in instances)
        {
            _hidHideService.AddBlockedInstanceId(instance);
            _blockedDeviceInstances.Add(instance);
        }
    }

    public static void UpdateInputEvents(bool remove = false)
    {
        // Keyboard
        if (!remove && HasActionKM())
        {
            if (!_keyEventRegistered)
            {
                InputCapture.Global.RegisterEvent(GlobalKeyEvent);
                _keyEventRegistered = true;
            }
        }
        else if (_keyEventRegistered)
        {
            InputCapture.Global.UnregisterEvent(GlobalKeyEvent);
            _keyEventRegistered = false;
        }

        // Mouse
        if (!remove && HasActionKM(false))
        {
            if (!_mouseEventRegistered)
            {
                InputCapture.Global.RegisterEvent(GlobalMouseEvent);
                _mouseEventRegistered = true;
            }
        }
        else if (_mouseEventRegistered)
        {
            InputCapture.Global.UnregisterEvent(GlobalMouseEvent);
            _mouseEventRegistered = false;
        }
    }

    private static bool HasActionKM(bool keyboard = true)
    {
        var input = keyboard ? "key_" : "mse_";

        foreach (var key in Settings.GetActionsKeys())
        {
            if (Settings.Value(key).StartsWith(input))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HandleMouseAction(string settingKey, ButtonCode key)
    {
        var resVal = Settings.Value(settingKey);
        return resVal.StartsWith("mse_") && (int)key == int.Parse(resVal.AsSpan(4));
    }

    private static void GlobalMouseEvent(object sender, EventSourceEventArgs<MouseEvent> e)
    {
        if (e.Data.ButtonDown?.Button is ButtonCode buttonDown)
        {
            if (HandleMouseAction("reset_mouse", buttonDown) &&
                Screen.PrimaryScreen is Screen primaryScreen)
            {
                WindowsInput.Simulate.Events()
                    .MoveTo(
                        primaryScreen.Bounds.Width / 2,
                        primaryScreen.Bounds.Height / 2
                    )
                    .Invoke();
            }

            bool activeGyro = HandleMouseAction("active_gyro", buttonDown);
            bool swapAB = HandleMouseAction("swap_ab", buttonDown);
            bool swapXY = HandleMouseAction("swap_xy", buttonDown);
            
            Mgr?.ChangeControllerSettings(activeGyro, swapAB, swapXY);
            
            return;
        }

        if (e.Data.ButtonUp?.Button is ButtonCode buttonUp)
        {
            bool activeGyro = HandleMouseAction("active_gyro", buttonUp);

            if (activeGyro)
            {
                Mgr?.DisableAllGyroscopes();
            }
        }
    }

    private static bool HandleKeyAction(string settingKey, KeyCode key)
    {
        var resVal = Settings.Value(settingKey);
        return resVal.StartsWith("key_") && (int)key == int.Parse(resVal.AsSpan(4));
    }

    private static void GlobalKeyEvent(object sender, EventSourceEventArgs<KeyboardEvent> e)
    {
        if (e.Data.KeyDown?.Key is KeyCode keyDown)
        {
            if (HandleKeyAction("reset_mouse", keyDown) &&
                Screen.PrimaryScreen is Screen primaryScreen)
            {
                WindowsInput.Simulate.Events()
                    .MoveTo(
                        primaryScreen.Bounds.Width / 2,
                        primaryScreen.Bounds.Height / 2
                    )
                    .Invoke();
            }

            bool activeGyro = HandleKeyAction("active_gyro", keyDown);
            bool swapAB = HandleKeyAction("swap_ab", keyDown);
            bool swapXY = HandleKeyAction("swap_xy", keyDown);
            
            Mgr?.ChangeControllerSettings(activeGyro, swapAB, swapXY);
            
            return;
        }

        if (e.Data.KeyUp?.Key is KeyCode keyUp)
        {
            bool activeGyro = HandleKeyAction("active_gyro", keyUp);

            if (activeGyro)
            {
                Mgr?.DisableAllGyroscopes();
            }
        }
    }

    public static async Task Stop()
    {
        if (!_isRunning)
        {
            return;
        }

        _isRunning = false;

        if (Mgr != null)
        {
            await Mgr.Stop();
        }

        UpdateInputEvents(true);
        InputCapture.Global.Dispose();

        EmClient?.Dispose();
        StopHIDHide();

        if (Server != null)
        {
            await Server.Stop();
        }
    }

    public static void AllowAnotherInstance()
    {
        _mutexInstance?.Close();
    }

    public static void StopHIDHide()
    {
        try
        {
            if (!_hidHideService.IsInstalled)
            {
                return;
            }

            _hidHideService.RemoveApplicationPath(Environment.ProcessPath);

            if (Config.PurgeAffectedDevices)
            {
                foreach (var instance in _blockedDeviceInstances)
                {
                    _hidHideService.RemoveBlockedInstanceId(instance);
                }
            }

            if (Config.HIDHideAlwaysOn)
            {
                return;
            }

            _hidHideService.IsActive = false;
        }
        catch (Exception e)
        {
            _logger?.Log("Unable to disable HIDHide.", e);
            return;
        }

        _logger?.Log("HIDHide is disabled.");
    }

    public static void UpdateThirdpartyControllers(List<SController> controllers)
    {
        ThirdpartyCons.Set(controllers);
    }

    private static void InitializeLogger()
    {
        try
        {
            _logger = new Logger(_logFilePath);
        }
        catch (Exception e)
        {
            Debug.Write($"Error initializing log file: {e}");
        }
    }

    private static async Task DisposeLogger()
    {
        if (_logger == null)
        {
            return;
        }

        await _logger.Close();
        _logger.Dispose();
    }

    private static void LogDebugInfos()
    {
        if (_logger == null)
        {
            return;
        }

        var programName = Application.ProductName;
        var osArch = Environment.Is64BitProcess ? "x64" : "x86";
        _logger?.Log($"{programName} {ProgramVersion}", Logger.LogLevel.Debug);
        _logger?.Log($"OS version: {Environment.OSVersion} {osArch}", Logger.LogLevel.Debug);
    }

    [STAThread]
    private static async Task Main()
    {
        Environment.CurrentDirectory = AppDomain.CurrentDomain.BaseDirectory;

        // Setting the culturesettings so float gets parsed correctly
        CultureInfo.CurrentCulture = new CultureInfo("en-US", false);

        // Set the correct DLL for the current OS
        SetupDlls();

        using (_mutexInstance = new Mutex(false, "Global\\" + _appGuid))
        {
            if (!_mutexInstance.WaitOne(0, false))
            {
                MessageBox.Show("Instance already running.", "BetterJoy");
                return;
            }

            InitializeLogger();

            try
            {
                LogDebugInfos();

                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                _form = new MainForm(_logger);
                Application.Run(_form);
            }
            finally
            {
                await DisposeLogger();
            }
        }
    }

    private static void SetupDlls()
    {
        var ok = NativeMethods.SetDefaultDllDirectories(NativeMethods.LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
        if (!ok)
        {
            var errorCode = Marshal.GetLastWin32Error();
            Console.WriteLine($"SetDefaultDllDirectories failed with error code: {errorCode}");
        }

        var archPath = $"{AppDomain.CurrentDomain.BaseDirectory}{(Environment.Is64BitProcess ? "x64" : "x86")}";
        var ret = NativeMethods.AddDllDirectory(archPath);
        if (ret == IntPtr.Zero)
        {
            var errorCode = Marshal.GetLastWin32Error();
            Console.WriteLine($"AddDllDirectory failed with error code: {errorCode}");
        }
    }

    public static async Task ApplyConfig()
    {
        var oldConfig = Config.Clone();
        Config.Update();

        if (oldConfig.MotionServer != Config.MotionServer)
        {
            if (Config.MotionServer)
            {
                Server.Start(Config.IP, Config.Port);
            }
            else
            {
                await Server.Stop();
            }
        }
        else if (!oldConfig.IP.Equals(Config.IP) ||
                 oldConfig.Port != Config.Port)
        {
            await Server.Stop();
            Server.Start(Config.IP, Config.Port);
        }

        if (oldConfig.UseHIDHide != Config.UseHIDHide)
        {
            if (Config.UseHIDHide)
            {
                StartHIDHide();
            }
            else
            {
                StopHIDHide();
            }
        }

        Mgr?.ApplyAllConfigs();
    }

    public static void SetSuspended(bool suspend)
    {
        IsSuspended = suspend;
    }
}
