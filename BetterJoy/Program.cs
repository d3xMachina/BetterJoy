#nullable disable
using BetterJoy.Collections;
using BetterJoy.Config;
using BetterJoy.Controller;
using BetterJoy.Exceptions;
using BetterJoy.Forms;
using BetterJoy.HIDApi.Exceptions;
using BetterJoy.HIDApi.Native;
using BetterJoy.Network.Server;
using Nefarius.Drivers.HidHide;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
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

public class JoyconManager
{
    private const ushort VendorId = 0x57e;
    private const ushort ProductL = 0x2006;
    private const ushort ProductR = 0x2007;
    private const ushort ProductPro = 0x2009;
    private const ushort ProductSNES = 0x2017;
    private const ushort ProductNES = 0x2007;
    private const ushort ProductFamicomI = 0x2007;
    private const ushort ProductFamicomII = 0x2007;
    private const ushort ProductN64 = 0x2019;

    private readonly Logger _logger;
    private readonly MainForm _form;

    private bool _isRunning = false;
    private CancellationTokenSource _ctsDevicesNotifications;

    public ConcurrentList<Joycon> Controllers { get; } = []; // connected controllers

    private readonly Channel<DeviceNotification> _channelDeviceNotifications;
    private Task _devicesNotificationTask;

    private readonly Lock _joinOrSplitJoyconLock = new();

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

    public JoyconManager(Logger logger, MainForm form)
    {
        _logger = logger;
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

        try
        {
            HIDApi.Manager.Init();

            HIDApi.Manager.DeviceNotificationReceived += OnDeviceNotification;
            HIDApi.Manager.StartDeviceNotifications();
        }
        catch (BadImageFormatException e)
        {
            _logger?.Log("Invalid hidapi.dll. (32 bits VS 64 bits)", e);
            return false;
        }
        catch (HIDApiInitFailedException e)
        {
            _logger?.Log("Could not initialize hidapi.", e);
            return false;
        }
        catch (HIDApiCallbackFailedException e)
        {
            _logger?.Log("Could not register hidapi callback.", e);
            HIDApi.Manager.Exit();
            return false;
        }

        _ctsDevicesNotifications = new CancellationTokenSource();
        _devicesNotificationTask = Task.Run(
            async () =>
            {
                try
                {
                    await ProcessDevicesNotifications(_ctsDevicesNotifications.Token);
                    _logger?.Log("Task devices notification finished.", Logger.LogLevel.Debug);
                }
                catch (OperationCanceledException) when (_ctsDevicesNotifications.IsCancellationRequested)
                {
                    _logger?.Log("Task devices notification canceled.", Logger.LogLevel.Debug);
                }
                catch (Exception e)
                {
                    _logger?.Log("Task devices notification error.", e);
                    throw;
                }
            }
        );
        _logger?.Log("Task devices notification started.", Logger.LogLevel.Debug);

        _isRunning = true;
        return true;
    }

    private void OnDeviceNotification(object sender, HIDApi.DeviceNotificationEventArgs e)
    {
        var notification = DeviceNotification.Type.Unknown;
        switch (e.DeviceEvent)
        {
            case HotplugEvent.DeviceArrived:
                notification = DeviceNotification.Type.Connected;
                break;
            case HotplugEvent.DeviceLeft:
                notification = DeviceNotification.Type.Disconnected;
                break;
        }

        var job = new DeviceNotification(notification, e.DeviceInfo);

        while (!_channelDeviceNotifications.Writer.TryWrite(job)) { }
    }

    private async Task ProcessDevicesNotifications(CancellationToken token)
    {
        var channelReader = _channelDeviceNotifications.Reader;

        while (await channelReader.WaitToReadAsync(token))
        {
            while (channelReader.TryRead(out var job))
            {
                token.ThrowIfCancellationRequested();

                switch (job.Notification)
                {
                    case DeviceNotification.Type.Connected:
                    {
                        var deviceInfos = (DeviceInfo)job.Data;
                        OnDeviceConnected(deviceInfos);
                        break;
                    }
                    case DeviceNotification.Type.Disconnected:
                    {
                        var deviceInfos = (DeviceInfo)job.Data;
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
        }
    }

    private void OnDeviceConnected(DeviceInfo info)
    {
        if (info.SerialNumber == null || GetControllerByPath(info.Path) != null)
        {
            return;
        }

        // Don't need to put NES or Famicom here because they are the same as the right Joycon
        bool validController = info is
        {
            VendorId: VendorId,
            ProductId: ProductL or ProductR or ProductPro or ProductSNES or ProductN64
        };

        SController thirdParty = null;

        // Check if it's a custom controller
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

        Joycon.ControllerType type;

        if (thirdParty == null)
        {
            switch (info.ProductId)
            {
                case ProductL:
                    type = Joycon.ControllerType.JoyconLeft;
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
                default:
                    _logger?.Log($"Invalid product ID: {info.ProductId}.", Logger.LogLevel.Error);
                    return;
            }
        }
        else
        {
            if (!Enum.IsDefined(typeof(Joycon.ControllerType), thirdParty.Type))
            {
                _logger?.Log($"Invalid third-party controller type: {thirdParty.Type}.", Logger.LogLevel.Error);
                return;
            }

            type = (Joycon.ControllerType)thirdParty.Type;
        }

        var isUSB = info.BusType == BusType.USB;

        OnDeviceConnected(info.Path, info.SerialNumber, type, isUSB, thirdParty != null);
    }

    private void OnDeviceConnected(string path, string serial, Joycon.ControllerType type, bool isUSB, bool isThirdparty, bool reconnect = false)
    {
        var device = HIDApi.Device.OpenPath(path);
        if (!device.IsValid)
        {
            // don't show an error message when the controller was dropped without hidapi callback notification (after standby for example)
            if (!reconnect)
            {
                _logger?.Log($"Unable to open device: {HIDApi.Manager.GetError()}.", Logger.LogLevel.Error);
            }

            return;
        }

        device.SetNonBlocking(1);

        var index = GetControllerIndex(type);
        var name = Joycon.GetControllerName(type);
        _logger?.Log($"[P{index + 1}] {name} connected.");

        // Add controller to block-list for HidHide
        Program.AddDeviceToBlocklist(device);

        var controller = new Joycon(
            _logger,
            _form,
            device,
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
            _logger?.Log($"[P{index + 1}] Could not connect.", e);
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
                _logger?.Log("Could not connect the virtual controller. Retrying...", e);

                ReconnectVirtualControllerDelayed(controller);
            }
        }

        controller.Begin();
    }

    private void OnDeviceDisconnected(DeviceInfo info)
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
                _logger?.Log("Could not connect the virtual controller for the unjoined joycon. Retrying...", e);

                ReconnectVirtualControllerDelayed(otherController);
            }
        }

        if (Controllers.Remove(controller) &&
            oldState > Joycon.Status.AttachError)
        {
            _form.RemoveController(controller);
        }

        var name = controller.GetControllerName();
        _logger?.Log($"[P{controller.PadId + 1}] {name} disconnected.");
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
            _logger?.Log($"[P{controller.PadId + 1}] Virtual controller reconnected.");
        }
        catch (Exception e)
        {
            _logger?.Log($"[P{controller.PadId + 1}] Could not reconnect the virtual controller. Retrying...", e);

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

    private int GetControllerIndex(Joycon.ControllerType type)
    {
        const int NbControllersMax = 8;
        var controllers = Controllers.OrderBy(c => c.PadId);

        // Get the ID next to a matching joycon that is not joined if possible
        if (type == Joycon.ControllerType.JoyconLeft)
        {
            var rightJoycons = controllers.Where(c => c.Type == Joycon.ControllerType.JoyconRight && !c.IsJoined);
            foreach (var joycon in rightJoycons)
            {
                if (joycon.PadId < 1)
                {
                    continue;
                }

                if (joycon.PadId > NbControllersMax - 1)
                {
                    break;
                }

                var isIdTaken = controllers.Any(c => c.PadId == joycon.PadId - 1);
                if (!isIdTaken)
                {
                    return joycon.PadId - 1;
                }
            }
        }
        else if (type == Joycon.ControllerType.JoyconRight)
        {
            var leftJoycons = controllers.Where(c => c.Type == Joycon.ControllerType.JoyconLeft && !c.IsJoined);
            foreach (var joycon in leftJoycons)
            {
                if (joycon.PadId >= NbControllersMax - 1)
                {
                    break;
                }

                var isIdTaken = controllers.Any(c => c.PadId == joycon.PadId + 1);
                if (!isIdTaken)
                {
                    return joycon.PadId + 1;
                }
            }
        }

        int freeId = 0;
        int firstFreeId = -1;

        while (true)
        {
            var isIdTaken = controllers.Any(c => c.PadId == freeId);

            // Free ID found, force joycons left/right to be next to each others in the same order if possible
            if (!isIdTaken)
            {
                if (firstFreeId == -1)
                {
                    firstFreeId = freeId;
                }

                var isValid = true;
                var prevController = controllers.FirstOrDefault(c => c.PadId == freeId - 1);
                var nextController = controllers.FirstOrDefault(c => c.PadId == freeId + 1);

                if (type == Joycon.ControllerType.JoyconLeft)
                {
                    if (// Permit filling after a disconnect when using many controllers in the case : Left Joycon - empty - Right Joycon
                        (prevController != null && prevController.Type == Joycon.ControllerType.JoyconLeft &&
                         (nextController == null || nextController.Type != Joycon.ControllerType.JoyconRight || nextController.IsJoined)) ||
                        (nextController != null && (nextController.Type != Joycon.ControllerType.JoyconRight || nextController.IsJoined)))
                    {
                        isValid = false;
                    }
                }
                else if (type == Joycon.ControllerType.JoyconRight)
                {
                    if (freeId < 1 ||
                        // Permit filling after a disconnect when using many controllers in the case : Left Joycon - empty - Right Joycon
                        (nextController != null && nextController.Type == Joycon.ControllerType.JoyconRight &&
                         (prevController == null || prevController.Type != Joycon.ControllerType.JoyconLeft || prevController.IsJoined)) ||
                        (prevController != null && (prevController.Type != Joycon.ControllerType.JoyconLeft || prevController.IsJoined)))
                    {
                        isValid = false;
                    }
                }
                else
                {
                    if ((prevController != null && prevController.Type == Joycon.ControllerType.JoyconLeft && !prevController.IsJoined) ||
                        (nextController != null && nextController.Type == Joycon.ControllerType.JoyconRight && !nextController.IsJoined))
                    {
                        isValid = false;
                    }
                }

                if (isValid)
                {
                    return freeId;
                }

                // Fill a free spot even if it doesn't meet the rule of having a left and right joycon next to each others
                if (freeId >= NbControllersMax && firstFreeId != -1 && firstFreeId < NbControllersMax)
                {
                    return firstFreeId;
                }
            }

            ++freeId;
        }
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

    public async Task Stop()
    {
        if (!_isRunning)
        {
            return;
        }

        _isRunning = false;

        _ctsDevicesNotifications.Cancel();

        try
        {
            HIDApi.Manager.StopDeviceNotifications();
        }
        catch (HIDApiCallbackFailedException e)
        {
            _logger?.Log("Could not deregister the device notifications callback.", e);
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

        HIDApi.Manager.Exit();
    }

    private static bool TryJoinJoycon(Joycon controller, Joycon otherController)
    {
        if (!otherController.IsJoycon ||
            otherController.Other != null || // already associated
            controller.IsLeft == otherController.IsLeft ||
            controller == otherController ||
            otherController.State < Joycon.Status.Attached)
        {
            return false;
        }

        controller.Other = otherController;
        otherController.Other = controller;

        controller.RequestSetLEDByPadID();
        otherController.RequestSetLEDByPadID();

        var rightController = controller.IsLeft ? otherController : controller;
        rightController.DisconnectViGEm();

        return true;
    }

    public bool JoinJoycon(Joycon controller, bool joinSelf = false)
    {
        if (!controller.IsJoycon)
        {
            return false;
        }

        lock (_joinOrSplitJoyconLock)
        {
            if (controller.Other != null)
            {
                return false;
            }

            if (joinSelf)
            {
                // hacky; implement check in Joycon.cs to account for this
                controller.Other = controller;

                return true;
            }

            // Join joycon with one next to it if possible
            {
                var otherPadId = controller.IsLeft ? controller.PadId + 1 : controller.PadId - 1;
                var otherController = Controllers.FirstOrDefault(c => c.PadId == otherPadId);

                if (otherController != null)
                {
                    if (TryJoinJoycon(controller, otherController))
                    {
                        return true;
                    }
                }
            }

            foreach (var otherController in Controllers.OrderBy(c => c.PadId))
            {
                if (!TryJoinJoycon(controller, otherController))
                {
                    continue;
                }

                return true;
            }
        }

        return false;
    }

    public bool SplitJoycon(Joycon controller, bool keep = true)
    {
        if (!controller.IsJoycon)
        {
            return false;
        }

        lock (_joinOrSplitJoyconLock)
        {
            var otherController = controller.Other;

            if (otherController == null)
            {
                return false;
            }

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
                _logger?.Log("Could not connect the virtual controller for the split joycon. Retrying...", e);

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
        }

        return true;
    }

    public bool JoinOrSplitJoycon(Joycon controller)
    {
        bool change = false;

        lock (_joinOrSplitJoyconLock)
        {
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
        }

        return change;
    }

    public static void ApplyConfig(Joycon controller, bool showErrors = true)
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
            if (HandleMouseAction("reset_mouse", button) &&
                Screen.PrimaryScreen is Screen primaryScreen)
            {
                WindowsInput.Simulate.Events()
                    .MoveTo(
                        primaryScreen.Bounds.Width / 2,
                        primaryScreen.Bounds.Height / 2
                    )
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
            if (HandleKeyAction("reset_mouse", key) &&
                Screen.PrimaryScreen is Screen primaryScreen)
            {
                WindowsInput.Simulate.Events()
                    .MoveTo(
                        primaryScreen.Bounds.Width / 2,
                        primaryScreen.Bounds.Height / 2
                    )
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

    public static string GetProgramVersion()
    {
        var programVersion = Assembly.GetExecutingAssembly().GetName().Version;
        return $"v{programVersion.Major}.{programVersion.Minor}.{programVersion.Build}";
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
        _logger?.Log($"{programName} {GetProgramVersion()}", Logger.LogLevel.Debug);
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

        bool showErrors = true;
        foreach (var controller in Mgr.Controllers)
        {
            JoyconManager.ApplyConfig(controller, showErrors);
            showErrors = false; // only show parsing errors once
        }
    }

    public static void SetSuspended(bool suspend)
    {
        IsSuspended = suspend;
    }
}
