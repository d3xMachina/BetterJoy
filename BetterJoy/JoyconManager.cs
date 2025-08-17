using BetterJoy.Collections;
using BetterJoy.Controller;
using BetterJoy.Forms;
using BetterJoy.HIDApi.Exceptions;
using BetterJoy.HIDApi.Native;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace BetterJoy;

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

    private readonly Logger? _logger;
    private readonly MainForm _form;

    [MemberNotNullWhen(returnValue: true,
        nameof(_ctsDevicesNotifications),
        nameof(_devicesNotificationTask))]
    public bool Running { get; private set; } = false;
    private CancellationTokenSource? _ctsDevicesNotifications;

    public ConcurrentList<Joycon> Controllers { get; } = []; // connected controllers

    private readonly Channel<DeviceNotification> _channelDeviceNotifications;
    private Task? _devicesNotificationTask;

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
        if (Running)
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

        Running = true;
        return true;
    }

    private void OnDeviceNotification(object? sender, HIDApi.DeviceNotificationEventArgs e)
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

        _3rdPartyControllers.SController? thirdParty = null;

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
                _form.JoinJoycon(controller, controller.Other!);
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

    private void OnDeviceDisconnected(Joycon? controller)
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
        Joycon? controller = GetControllerByPath(deviceIdentifier.Path);
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
        Joycon? controller = GetControllerByPath(deviceIdentifier.Path);
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
        Joycon? controller = GetControllerByPath(deviceIdentifier.Path);
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
        Task.Delay(delayMs).ContinueWith(_ => ReconnectVirtualController(controller));
    }

    private void ReconnectVirtualController(Joycon controller)
    {
        var writer = _channelDeviceNotifications.Writer;
        var identifier = new ControllerIdentifier(controller);
        var notification = new DeviceNotification(DeviceNotification.Type.VirtualControllerErrored, identifier);
        while (!writer.TryWrite(notification)) { }
    }

    private void OnControllerStateChanged(object? sender, Joycon.StateChangedEventArgs e)
    {
        if (sender is not Joycon controller || 
            _ctsDevicesNotifications is not {IsCancellationRequested: false})
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
        Task.Delay(delayMs).ContinueWith(_ => ReconnectController(controller));
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

    private Joycon? GetControllerByPath(string path)
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
        if (!Running)
        {
            return;
        }

        Running = false;

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
                    _form.JoinJoycon(controller, controller.Other!);
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
