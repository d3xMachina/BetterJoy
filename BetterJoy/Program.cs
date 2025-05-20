using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows.Forms;
using BetterJoy.Collections;
using BetterJoy.Config;
using BetterJoy.Exceptions;
using Nefarius.Drivers.HidHide;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;
using WindowsInput;
using WindowsInput.Events;
using WindowsInput.Events.Sources;
using static BetterJoy._3rdPartyControllers;

namespace BetterJoy;

public struct ControllerIdentifier
{
    public readonly string Path;
    public readonly long TimestampCreation;

    public ControllerIdentifier(Joycon controller)
    {
        Path = controller.Path;
        TimestampCreation = controller.TimestampCreation;
    }
}

public class JoyconManager
{
    private const ushort VendorId = 0x57e;
    private const ushort ProductL = 0x2006;
    private const ushort ProductR = 0x2007;
    private const ushort ProductPro = 0x2009;
    private const ushort ProductSNES = 0x2017;
    private const ushort ProductNES = 0x2007;
    private const ushort ProductN64 = 0x2019;

    public readonly bool EnableIMU = true;
    public readonly bool EnableLocalize = false;

    private readonly MainForm _form;

    private bool _isRunning = false;
    private CancellationTokenSource _ctsDevicesNotifications;

    public ConcurrentList<Joycon> Controllers { get; } = new(); // connected controllers

    private readonly Channel<DeviceNotification> _channelDeviceNotifications;

    private int _hidCallbackHandle = 0;
    private Task _devicesNotificationTask;

    private class DeviceNotification
    {
        public enum Type
        {
            Unknown,
            Connected,
            Disconnected,
            ForceDisconnected,
            Errored,
            VirtualControllerErrored
        }

        public readonly Type Notification;
        public readonly object Data;

        public DeviceNotification(Type notification, object data) // data must be immutable
        {
            Notification = notification;
            Data = data;
        }
    }

    public JoyconManager(MainForm form)
    {
        _form = form;

        _channelDeviceNotifications = Channel.CreateUnbounded<DeviceNotification>(
            new UnboundedChannelOptions
            {
                SingleWriter = false,
                SingleReader = true,
                AllowSynchronousContinuations = false
            }
        );
    }

    public bool Start()
    {
        if (_isRunning)
        {
            return true;
        }

        int ret;

        try
        {
            ret = HIDApi.Init();
        }
        catch (BadImageFormatException e)
        {
            _form.Log($"Invalid hidapi.dll. (32 bits VS 64 bits)", e);
            return false;
        }
        
        if (ret != 0)
        {
            _form.Log("Could not initialize hidapi.", Logger.LogLevel.Error);
            return false;
        }

        ret = HIDApi.HotplugRegisterCallback(
            0x0,
            0x0,
            (int)(HIDApi.HotplugEvent.DeviceArrived | HIDApi.HotplugEvent.DeviceLeft),
            (int)HIDApi.HotplugFlag.Enumerate,
            OnDeviceNotification,
            _channelDeviceNotifications.Writer,
            out _hidCallbackHandle
        );

        if (ret != 0)
        {
            _form.Log("Could not register hidapi callback.", Logger.LogLevel.Error);
            HIDApi.Exit();
            return false;
        }

        _ctsDevicesNotifications = new CancellationTokenSource();
        _devicesNotificationTask = Task.Run(
            async () =>
            {
                try
                {
                    await ProcessDevicesNotifications(_ctsDevicesNotifications.Token);
                    _form.Log("Task devices notification finished.", Logger.LogLevel.Debug);
                }
                catch (OperationCanceledException) when (_ctsDevicesNotifications.IsCancellationRequested)
                {
                    _form.Log("Task devices notification canceled.", Logger.LogLevel.Debug);
                }
                catch (Exception e)
                {
                    _form.Log("Task devices notification error.", e);
                    throw;
                }
            }
        );
        _form.Log("Task devices notification started.", Logger.LogLevel.Debug);

        _isRunning = true;
        return true;
    }

    private static int OnDeviceNotification(int callbackHandle, HIDApi.HIDDeviceInfo deviceInfo, int ev, object pUserData)
    {
        var channelWriter = (ChannelWriter<DeviceNotification>)pUserData;
        var deviceEvent = (HIDApi.HotplugEvent)ev;

        var notification = DeviceNotification.Type.Unknown;
        switch (deviceEvent)
        {
            case HIDApi.HotplugEvent.DeviceArrived:
                notification = DeviceNotification.Type.Connected;
                break;
            case HIDApi.HotplugEvent.DeviceLeft:
                notification = DeviceNotification.Type.Disconnected;
                break;
        }

        var job = new DeviceNotification(notification, deviceInfo);

        while (!channelWriter.TryWrite(job)) { }

        return 0;
    }

    private async Task ProcessDevicesNotifications(CancellationToken token)
    {
        var channelReader = _channelDeviceNotifications.Reader;

        while (await channelReader.WaitToReadAsync(token))
        {
            bool read;
            do
            {
                token.ThrowIfCancellationRequested();
                read = channelReader.TryRead(out var job);

                if (read)
                {
                    switch (job.Notification)
                    {
                        case DeviceNotification.Type.Connected:
                        {
                            var deviceInfos = (HIDApi.HIDDeviceInfo)job.Data;
                            OnDeviceConnected(deviceInfos);
                            break;
                        }
                        case DeviceNotification.Type.Disconnected:
                        {
                            var deviceInfos = (HIDApi.HIDDeviceInfo)job.Data;
                            OnDeviceDisconnected(deviceInfos);
                            break;
                        }
                        case DeviceNotification.Type.ForceDisconnected:
                        {
                            var deviceIdentifier = (ControllerIdentifier)job.Data;
                            OnDeviceDisconnected(deviceIdentifier);
                            break;
                        }
                        case DeviceNotification.Type.Errored:
                        {
                            var deviceIdentifier = (ControllerIdentifier)job.Data;
                            OnDeviceErrored(deviceIdentifier);
                            break;
                        }
                        case DeviceNotification.Type.VirtualControllerErrored:
                        {
                            var deviceIdentifier = (ControllerIdentifier)job.Data;
                            OnVirtualControllerErrored(deviceIdentifier);
                            break;
                        }
                    }
                }
            } while (read);
        }
    }

    private void OnDeviceConnected(HIDApi.HIDDeviceInfo info)
    {
        if (info.SerialNumber == null || GetControllerByPath(info.Path) != null)
        {
            return;
        }

        var validController = (info.ProductId == ProductL || info.ProductId == ProductR ||
                               info.ProductId == ProductPro || info.ProductId == ProductSNES ||
                               info.ProductId == ProductN64) &&
                              info.VendorId == VendorId;

        // check if it's a custom controller
        SController thirdParty = null;
        foreach (var v in Program.ThirdpartyCons)
        {
            if (info.VendorId == v.VendorId &&
                info.ProductId == v.ProductId &&
                info.SerialNumber == v.SerialNumber)
            {
                validController = true;
                thirdParty = v;
                break;
            }
        }

        if (!validController)
        {
            return;
        }

        var prodId = thirdParty == null ? info.ProductId : TypeToProdId(thirdParty.Type);
        if (prodId == 0)
        {
            // controller was not assigned a type
            return;
        }

        bool isUSB = info.BusType == HIDApi.BusType.USB;
        var type = Joycon.ControllerType.JoyconLeft;

        switch (prodId)
        {
            case ProductL:
                break;
            case ProductR:
                type = Joycon.ControllerType.JoyconRight;
                break;
            case ProductPro:
                type = Joycon.ControllerType.Pro;
                break;
            case ProductSNES:
                type = Joycon.ControllerType.SNES;
                break;
            case ProductN64:
                type = Joycon.ControllerType.N64;
                break;
        }

        OnDeviceConnected(info.Path, info.SerialNumber, type, isUSB, thirdParty != null);
    }

    private void OnDeviceConnected(string path, string serial, Joycon.ControllerType type, bool isUSB, bool isThirdparty, bool reconnect = false)
    {
        var handle = HIDApi.OpenPath(path);
        if (handle == IntPtr.Zero)
        {
            // don't show an error message when the controller was dropped without hidapi callback notification (after standby for example)
            if (!reconnect)
            {
                _form.Log($"Unable to open device: {HIDApi.Error(IntPtr.Zero)}.", Logger.LogLevel.Error);
            }

            return;
        }

        HIDApi.SetNonBlocking(handle, 1);

        var index = GetControllerIndex();
        var name = Joycon.GetControllerName(type);
        _form.Log($"[P{index + 1}] {name} connected.");

        // Add controller to block-list for HidHide
        Program.AddDeviceToBlocklist(handle);

        var controller = new Joycon(
            _form,
            handle,
            EnableIMU,
            EnableLocalize && EnableIMU,
            path,
            serial,
            isUSB,
            index,
            type,
            isThirdparty
        );
        controller.StateChanged += OnControllerStateChanged;

        // Connect device straight away
        try
        {
            controller.Attach();
        }
        catch (Exception e)
        {
            _form.Log($"[P{index + 1}] Could not connect.", e);
            return;
        }
        finally
        {
            Controllers.Add(controller);
        }
        
        _form.AddController(controller);

        // attempt to auto join-up joycons on connection
        var doNotRejoin = controller.Config.DoNotRejoin;
        if (doNotRejoin != Joycon.Orientation.Horizontal)
        {
            bool joinSelf = doNotRejoin != Joycon.Orientation.None;
            if (JoinJoycon(controller, joinSelf))
            {
                _form.JoinJoycon(controller, controller.Other);
            }
        }

        controller.SetCalibration(_form.Config.AllowCalibration);

        if (!controller.IsJoined || controller.IsLeft)
        {
            try
            {
                controller.ConnectViGEm();
            }
            catch (Exception e)
            {
                _form.Log($"Could not connect the virtual controller. Retrying...", e);

                ReconnectVirtualControllerDelayed(controller);
            }
        }

        controller.Begin();
    }

    private void OnDeviceDisconnected(HIDApi.HIDDeviceInfo info)
    {
        var controller = GetControllerByPath(info.Path);

        OnDeviceDisconnected(controller);
    }

    private void OnDeviceDisconnected(Joycon controller)
    {
        if (controller == null)
        {
            return;
        }

        Joycon.Status oldState = controller.State;
        controller.StateChanged -= OnControllerStateChanged;

        if (controller.IsDeviceReady)
        {
            // change the controller state to avoid trying to send command to it
            controller.Drop(false, false);
        }
        
        controller.Detach();

        var otherController = controller.Other;

        if (otherController != null && otherController != controller)
        {
            otherController.Other = null; // The other of the other is the joycon itself
            otherController.RequestSetLEDByPadID();

            try
            {
                otherController.ConnectViGEm();
            }
            catch (Exception e)
            {
                _form.Log($"Could not connect the virtual controller for the unjoined joycon. Retrying...", e);

                ReconnectVirtualControllerDelayed(otherController);
            }
        }

        if (Controllers.Remove(controller) &&
            oldState > Joycon.Status.AttachError)
        {
            _form.RemoveController(controller);
        }

        var name = controller.GetControllerName();
        _form.Log($"[P{controller.PadId + 1}] {name} disconnected.");
    }

    private void OnDeviceDisconnected(ControllerIdentifier deviceIdentifier)
    {
        Joycon controller = GetControllerByPath(deviceIdentifier.Path);
        if (controller == null ||
            controller.TimestampCreation != deviceIdentifier.TimestampCreation)
        {
            return;
        }

        if (controller.IsDeviceReady)
        {
            // device not dropped anymore (after a reset or a reconnection from the system)
            return;
        }
        
        OnDeviceDisconnected(controller);
    }

    private void OnDeviceErrored(ControllerIdentifier deviceIdentifier)
    {
        Joycon controller = GetControllerByPath(deviceIdentifier.Path);
        if (controller == null ||
            controller.TimestampCreation != deviceIdentifier.TimestampCreation)
        {
            return;
        }

        if (controller.IsDeviceReady)
        {
            // device not in error anymore (after a reset or a reconnection from the system)
            return;
        }
        
        OnDeviceDisconnected(controller);
        OnDeviceConnected(controller.Path, controller.SerialNumber, controller.Type, controller.IsUSB, controller.IsThirdParty, true);
    }

    private void OnVirtualControllerErrored(ControllerIdentifier deviceIdentifier)
    {
        Joycon controller = GetControllerByPath(deviceIdentifier.Path);
        if (controller == null ||
            controller.TimestampCreation != deviceIdentifier.TimestampCreation)
        {
            return;
        }

        if (!controller.IsDeviceReady ||
            (controller.IsJoined && !controller.IsLeft) ||
            controller.IsViGEmSetup())
        {
            return;
        }

        try
        {
            controller.ConnectViGEm();
            _form.Log($"[P{controller.PadId + 1}] Virtual controller reconnected.");
        }
        catch (Exception e)
        {
            _form.Log($"[P{controller.PadId + 1}] Could not reconnect the virtual controller. Retrying...", e);

            ReconnectVirtualControllerDelayed(controller);
        }
    }

    private void ReconnectVirtualControllerDelayed(Joycon controller, int delayMs = 2000)
    {
        Task.Delay(delayMs).ContinueWith(t => ReconnectVirtualController(controller));
    }

    private void ReconnectVirtualController(Joycon controller)
    {
        var writer = _channelDeviceNotifications.Writer;
        var identifier = new ControllerIdentifier(controller);
        var notification = new DeviceNotification(DeviceNotification.Type.VirtualControllerErrored, identifier);
        while (!writer.TryWrite(notification)) { }
    }

    private void OnControllerStateChanged(object sender, Joycon.StateChangedEventArgs e)
    {
        if (sender == null || _ctsDevicesNotifications.IsCancellationRequested)
        {
            return;
        }

        var controller = (Joycon)sender;
        if (controller == null)
        {
            return;
        }

        switch (e.State)
        {
            case Joycon.Status.AttachError:
            case Joycon.Status.Errored:
                ReconnectControllerDelayed(controller);
                break;
            case Joycon.Status.Dropped:
                DisconnectController(controller);
                break;
        }
    }
    private void ReconnectControllerDelayed(Joycon controller, int delayMs = 2000)
    {
        Task.Delay(delayMs).ContinueWith(t => ReconnectController(controller));
    }

    private void ReconnectController(Joycon controller)
    {
        var writer = _channelDeviceNotifications.Writer;
        var identifier = new ControllerIdentifier(controller);
        var notification = new DeviceNotification(DeviceNotification.Type.Errored, identifier);
        while (!writer.TryWrite(notification)) { }
    }

    private void DisconnectController(Joycon controller)
    {
        var writer = _channelDeviceNotifications.Writer;
        var identifier = new ControllerIdentifier(controller);
        var notification = new DeviceNotification(DeviceNotification.Type.ForceDisconnected, identifier);
        while (!writer.TryWrite(notification)) { }
    }

    private int GetControllerIndex()
    {
        List<int> ids = new();
        foreach (var controller in Controllers)
        {
            ids.Add(controller.PadId);
        }
        ids.Sort();

        int freeId = 0;

        foreach (var id in ids)
        {
            if (id != freeId)
            {
                break;
            }
            ++freeId;
        }

        return freeId;
    }

    private Joycon GetControllerByPath(string path)
    {
        foreach (var controller in Controllers)
        {
            if (controller.Path == path)
            {
                return controller;
            }
        }

        return null;
    }

    private ushort TypeToProdId(byte type)
    {
        switch (type)
        {
            case 1:
                return ProductPro;
            case 2:
                return ProductL;
            case 3:
                return ProductR;
            case 4:
                return ProductSNES;
            case 5:
                return ProductN64;
            case 6:
                return ProductNES;
        }

        return 0;
    }

    public async Task Stop()
    {
        if (!_isRunning)
        {
            return;
        }
        _isRunning = false;

        _ctsDevicesNotifications.Cancel();

        if (_hidCallbackHandle != 0)
        {
            HIDApi.HotplugDeregisterCallback(_hidCallbackHandle);
        }
        
        await _devicesNotificationTask;
        
        foreach (var controller in Controllers)
        {
            controller.StateChanged -= OnControllerStateChanged;

            if (controller.Config.AutoPowerOff && !controller.IsUSB)
            {
                controller.RequestPowerOff();
            }
        }
        
        Stopwatch timeSincePowerOff = Stopwatch.StartNew();
        int timeoutPowerOff = 1800; // a bit of extra time to have 3 attempts

        foreach (var controller in Controllers)
        {
            if (controller.Config.AutoPowerOff && !controller.IsUSB)
            {
                controller.WaitPowerOff(timeoutPowerOff);

                timeoutPowerOff -= (int)timeSincePowerOff.ElapsedMilliseconds;
                if (timeoutPowerOff < 0)
                {
                    timeoutPowerOff = 0;
                }
            }
            
            controller.Detach();
        }
        
        _ctsDevicesNotifications.Dispose();

        HIDApi.Exit();
    }

    public bool JoinJoycon(Joycon controller, bool joinSelf = false)
    {
        if (!controller.IsJoycon || controller.Other != null)
        {
            return false;
        }

        if (joinSelf)
        {
            // hacky; implement check in Joycon.cs to account for this
            controller.Other = controller;

            return true;
        }

        foreach (var otherController in Controllers)
        {
            if (!otherController.IsJoycon ||
                otherController.Other != null || // already associated
                controller.IsLeft == otherController.IsLeft ||
                controller == otherController ||
                otherController.State < Joycon.Status.Attached)
            {
                continue;
            }

            controller.Other = otherController;
            otherController.Other = controller;

            controller.RequestSetLEDByPadID();
            otherController.RequestSetLEDByPadID();

            var rightController = controller.IsLeft ? otherController : controller;
            rightController.DisconnectViGEm();

            return true;
        }

        return false;
    }

    public bool SplitJoycon(Joycon controller, bool keep = true)
    {
        if (!controller.IsJoycon || controller.Other == null)
        {
            return false;
        }

        var otherController = controller.Other;

        // Reenable vigem for the joined controller
        try
        {
            if (keep)
            {
                controller.ConnectViGEm();
            }
            otherController.ConnectViGEm();
        }
        catch (Exception e)
        {
            _form.Log($"Could not connect the virtual controller for the split joycon. Retrying...", e);

            if (keep && !controller.IsViGEmSetup())
            {
                ReconnectVirtualControllerDelayed(controller);
            }

            if (controller != otherController &&
                !otherController.IsViGEmSetup())
            {
                ReconnectVirtualControllerDelayed(otherController);
            }
        }

        controller.Other = null;
        otherController.Other = null;

        controller.RequestSetLEDByPadID();
        otherController.RequestSetLEDByPadID();

        return true;
    }

    public bool JoinOrSplitJoycon(Joycon controller)
    {
        bool change = false;

        if (controller.Other == null)
        {
            int nbJoycons = Controllers.Count(j => j.IsJoycon);

            // when we want to have a single joycon in vertical mode
            bool joinSelf = nbJoycons == 1 || controller.Config.DoNotRejoin != Joycon.Orientation.None;

            if (JoinJoycon(controller, joinSelf))
            {
                _form.JoinJoycon(controller, controller.Other);
                change = true;
            }
        }
        else 
        {
            Joycon other = controller.Other;

            if (SplitJoycon(controller))
            {
                _form.SplitJoycon(controller, other);
                change = true;
            }
        }

        return change;
    }

    public void ApplyConfig(Joycon controller, bool showErrors = true)
    {
        controller.ApplyConfig(showErrors);
    }
}

internal class Program
{
    public static PhysicalAddress BtMac = new([0, 0, 0, 0, 0, 0]);
    public static UdpServer Server;

    public static ViGEmClient EmClient;

    public static JoyconManager Mgr;

    private static MainForm _form;

    public static readonly ConcurrentList<SController> ThirdpartyCons = new();

    public static ProgramConfig Config;

    private static readonly HidHideControlService _hidHideService = new();
    private static readonly HashSet<string> BlockedDeviceInstances = new();

    private static bool _isRunning;
    public static bool IsSuspended { get; private set; }

    private static readonly string AppGuid = "1bf709e9-c133-41df-933a-c9ff3f664c7b"; // randomly-generated
    private static Mutex _mutexInstance;

    public static void Start()
    {
        Config = new(_form);
        Config.Update();

        StartHIDHide();

        try
        {
            EmClient = new ViGEmClient(); // Manages emulated XInput
        }
        catch (VigemBusNotFoundException e)
        {
            _form.Log("Could not connect to VIGEmBus. Make sure VIGEmBus driver is installed correctly.", e);
        }
        catch (VigemBusAccessFailedException e)
        {
            _form.Log("Could not connect to VIGEmBus. VIGEmBus is busy. Try restarting your computer or reinstalling VIGEmBus driver.", e);
        }
        catch (VigemBusVersionMismatchException e)
        {
            _form.Log("Could not connect to VIGEmBus. The installed VIGEmBus driver is not compatible. Install a newer version of VIGEmBus driver.", e);
        }
        catch (VigemAllocFailedException e)
        {
            _form.Log("Could not connect to VIGEmBus. Allocation failed. Try restarting your computer or reinstalling VIGEmBus driver.", e);
        }
        catch (VigemAlreadyConnectedException e)
        {
            // should not happen
            _form.Log("VIGEmBus is already connected.", e);
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

        Mgr = new JoyconManager(_form);
        Server = new UdpServer(_form, Mgr.Controllers);

        if (!Config.MotionServer)
        {
            _form.Log("Motion server is OFF.");
        }
        else
        {
            Server.Start(Config.IP, Config.Port);
        }

        InputCapture.Global.RegisterEvent(GlobalKeyEvent);
        InputCapture.Global.RegisterEvent(GlobalMouseEvent);

        _form.Log("All systems go.");
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
                _form.Log("HIDHide is not installed.", Logger.LogLevel.Warning);
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
            _form.Log($"Unable to start HIDHide.", e);
            return;
        }

        _form.Log("HIDHide is enabled.");
    }

    public static void AddDeviceToBlocklist(IntPtr handle)
    {
        try
        {
            if (!_hidHideService.IsActive)
            {
                return;
            }

            var devices = new List<string>();

            var instance = HIDApi.GetInstance(handle);
            if (instance.Length == 0)
            {
                _form.Log("Unable to get device instance.", Logger.LogLevel.Error);
            }
            else
            {
                devices.Add(instance);
            }

            var parentInstance = HIDApi.GetParentInstance(handle);
            if (parentInstance.Length == 0)
            {
                _form.Log("Unable to get device parent instance.", Logger.LogLevel.Error);
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
            _form.Log($"Unable to add controller to block-list.", e);
        }
    }

    public static void BlockDeviceInstances(IList<string> instances)
    {
        foreach (var instance in instances)
        {
            _hidHideService.AddBlockedInstanceId(instance);
            BlockedDeviceInstances.Add(instance);
        }
    }

    private static bool HandleMouseAction(string settingKey, ButtonCode? key)
    {
        var resVal = Settings.Value(settingKey);
        return resVal.StartsWith("mse_") && (int)key == int.Parse(resVal.AsSpan(4));
    }

    private static void GlobalMouseEvent(object sender, EventSourceEventArgs<MouseEvent> e)
    {
        ButtonCode? button = e.Data.ButtonDown?.Button;

        if (button != null)
        {
            if (HandleMouseAction("reset_mouse", button))
            {
                Simulate.Events()
                        .MoveTo(Screen.PrimaryScreen.Bounds.Width / 2, Screen.PrimaryScreen.Bounds.Height / 2)
                        .Invoke();
            }

            bool activeGyro = HandleMouseAction("active_gyro", button);
            bool swapAB = HandleMouseAction("swap_ab", button);
            bool swapXY = HandleMouseAction("swap_xy", button);

            if (activeGyro || swapAB || swapXY)
            {
                foreach (var controller in Mgr.Controllers)
                {
                    if (activeGyro) controller.ActiveGyro = true;
                    if (swapAB) controller.Config.SwapAB = !controller.Config.SwapAB;
                    if (swapXY) controller.Config.SwapXY = !controller.Config.SwapXY;
                }
            }
            return;
        }

        button = e.Data.ButtonUp?.Button;

        if (button != null)
        {
            bool activeGyro = HandleMouseAction("active_gyro", button);

            if (activeGyro)
            {
                foreach (var controller in Mgr.Controllers)
                {
                    controller.ActiveGyro = false;
                }
            }
        }
    }

    private static bool HandleKeyAction(string settingKey, KeyCode? key)
    {
        var resVal = Settings.Value(settingKey);
        return resVal.StartsWith("key_") && (int)key == int.Parse(resVal.AsSpan(4));
    }

    private static void GlobalKeyEvent(object sender, EventSourceEventArgs<KeyboardEvent> e)
    {
        KeyCode? key = e.Data.KeyDown?.Key;

        if (key != null)
        {
            if (HandleKeyAction("reset_mouse", key))
            {
                Simulate.Events()
                        .MoveTo(Screen.PrimaryScreen.Bounds.Width / 2, Screen.PrimaryScreen.Bounds.Height / 2)
                        .Invoke();
            }

            bool activeGyro = HandleKeyAction("active_gyro", key);
            bool swapAB = HandleKeyAction("swap_ab", key);
            bool swapXY = HandleKeyAction("swap_xy", key);

            if (activeGyro || swapAB || swapXY)
            {
                foreach (var controller in Mgr.Controllers)
                {
                    if (activeGyro) controller.ActiveGyro = true;
                    if (swapAB) controller.Config.SwapAB = !controller.Config.SwapAB;
                    if (swapXY) controller.Config.SwapXY = !controller.Config.SwapXY;
                }
            }
            return;
        }

        key = e.Data.KeyUp?.Key;

        if (key != null)
        {
            bool activeGyro = HandleKeyAction("active_gyro", key);

            if (activeGyro)
            {
                foreach (var controller in Mgr.Controllers)
                {
                    controller.ActiveGyro = false;
                }
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

        InputCapture.Global.UnregisterEvent(GlobalKeyEvent);
        InputCapture.Global.UnregisterEvent(GlobalMouseEvent);
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
                foreach (var instance in BlockedDeviceInstances)
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
            _form.Log($"Unable to disable HIDHide.", e);
            return;
        }

        _form.Log("HIDHide is disabled.");
    }

    public static void UpdateThirdpartyControllers(List<SController> controllers)
    {
        ThirdpartyCons.Set(controllers);
    }

    [STAThread]
    private static void Main(string[] args)
    {
        Environment.CurrentDirectory = AppDomain.CurrentDomain.BaseDirectory;

        // Setting the culturesettings so float gets parsed correctly
        CultureInfo.CurrentCulture = new CultureInfo("en-US", false);

        // Set the correct DLL for the current OS
        SetupDlls();

        using (_mutexInstance = new Mutex(false, "Global\\" + AppGuid))
        {
            if (!_mutexInstance.WaitOne(0, false))
            {
                MessageBox.Show("Instance already running.", "BetterJoy");
                return;
            }

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            _form = new MainForm();
            Application.Run(_form);
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

        bool showErrors = true;
        foreach (var controller in Mgr.Controllers)
        {
            Mgr.ApplyConfig(controller, showErrors);
            showErrors = false; // only show parsing errors once
        }
    }

    public static void SetSuspended(bool suspend)
    {
        IsSuspended = suspend;
    }
}
