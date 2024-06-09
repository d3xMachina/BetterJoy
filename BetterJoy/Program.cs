using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows.Forms;
using BetterJoy.Collections;
using BetterJoy.Exceptions;
using Nefarius.Drivers.HidHide;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;
using WindowsInput;
using WindowsInput.Events;
using WindowsInput.Events.Sources;
using static BetterJoy._3rdPartyControllers;

namespace BetterJoy
{
    public class JoyconManager
    {
        private const ushort VendorId = 0x57e;
        private const ushort ProductL = 0x2006;
        private const ushort ProductR = 0x2007;
        private const ushort ProductPro = 0x2009;
        private const ushort ProductSNES = 0x2017;

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
            catch (BadImageFormatException)
            {
                _form.AppendTextBox($"Invalid hidapi.dll (32 bits VS 64 bits)");
                return false;
            }
            
            if (ret != 0)
            {
                _form.AppendTextBox("Could not initialize hidapi");
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
                _form.AppendTextBox("Could not register hidapi callback");
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
                    }
                    catch (OperationCanceledException) when (_ctsDevicesNotifications.IsCancellationRequested) { }
                }
            );

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
                            case DeviceNotification.Type.Errored:
                            {
                                var devicePath = (string)job.Data;
                                OnDeviceErrored(devicePath);
                                break;
                            }
                            case DeviceNotification.Type.VirtualControllerErrored:
                            {
                                var devicePath = (string)job.Data;
                                OnVirtualControllerErrored(devicePath);
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
                                   info.ProductId == ProductPro || info.ProductId == ProductSNES) &&
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
            }

            OnDeviceConnected(info.Path, info.SerialNumber, type, isUSB, thirdParty != null);
        }

        private void OnDeviceConnected(string path, string serial, Joycon.ControllerType type, bool isUSB, bool isThirdparty, bool reconnect = false)
        {
            var handle = HIDApi.OpenPath(path);
            if (handle == IntPtr.Zero)
            {
                // don't show an error message when the controller was dropped without hidapi callback notification (after standby by example)
                if (!reconnect)
                {
                    _form.AppendTextBox($"Unable to open device: {HIDApi.Error(IntPtr.Zero)}");
                }

                return;
            }

            HIDApi.SetNonBlocking(handle, 1);

            var index = GetControllerIndex();
            var name = Joycon.GetControllerName(type);
            _form.AppendTextBox($"[P{index + 1}] {name} connected.");

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
                _form.AppendTextBox($"[P{index + 1}] Could not connect {e.Display()}.");
                return;
            }
            finally
            {
                Controllers.Add(controller);
            }
            
            _form.AddController(controller);

            // attempt to auto join-up joycons on connection
            if (!controller.Config.DoNotRejoin && JoinJoycon(controller))
            {
                _form.JoinJoycon(controller, controller.Other);
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
                    _form.AppendTextBox($"Could not connect the virtual controller {e.Display()}. Retrying...");

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
            controller.Detach();

            var otherController = controller.Other;

            if (otherController != null && otherController != controller)
            {
                otherController.Other = null; // The other of the other is the joycon itself
                SetLEDByPadID(otherController);

                try
                {
                    otherController.ConnectViGEm();
                }
                catch (Exception e)
                {
                    _form.AppendTextBox($"Could not connect the virtual controller for the unjoined joycon {e.Display()}. Retrying...");

                    ReconnectVirtualControllerDelayed(otherController);
                }
            }

            if (Controllers.Remove(controller) &&
                oldState > Joycon.Status.AttachError)
            {
                _form.RemoveController(controller);
            }

            var name = controller.GetControllerName();
            _form.AppendTextBox($"[P{controller.PadId + 1}] {name} disconnected.");
        }

        private void OnDeviceErrored(string devicePath)
        {
            Joycon controller = GetControllerByPath(devicePath);
            if (controller == null)
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

        private void OnVirtualControllerErrored(string devicePath)
        {
            Joycon controller = GetControllerByPath(devicePath);
            if (controller == null)
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
                _form.AppendTextBox($"[P{controller.PadId + 1}] Virtual controller reconnected.");
            }
            catch (Exception e)
            {
                _form.AppendTextBox($"[P{controller.PadId + 1}] Could not reconnect the virtual controller {e.Display()}. Retrying...");

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
            var notification = new DeviceNotification(DeviceNotification.Type.VirtualControllerErrored, controller.Path);
            while (!writer.TryWrite(notification)) { }
        }

        private void OnControllerStateChanged(object sender, Joycon.StateChangedEventArgs e)
        {
            if (sender == null)
            {
                return;
            }

            var controller = (Joycon)sender;
            var writer = _channelDeviceNotifications.Writer;

            switch (e.State)
            {
                case Joycon.Status.AttachError:
                case Joycon.Status.Errored:
                    var notification = new DeviceNotification(DeviceNotification.Type.Errored, controller.Path);
                    while (!writer.TryWrite(notification)) { }
                    break;
            }
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

            _ctsDevicesNotifications.Dispose();

            foreach (var controller in Controllers)
            {
                controller.StateChanged -= OnControllerStateChanged;

                if (controller.Config.AutoPowerOff && !controller.IsUSB)
                {
                    controller.PowerOff();
                }

                controller.Detach();
            }

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

                SetLEDByPadID(controller);
                SetLEDByPadID(otherController);

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
                _form.AppendTextBox($"Could not connect the virtual controller for the split joycon {e.Display()}. Retrying...");

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

            SetLEDByPadID(controller);
            SetLEDByPadID(otherController);

            return true;
        }

        public bool JoinOrSplitJoycon(Joycon controller)
        {
            bool change = false;

            if (controller.Other == null)
            {
                int nbJoycons = Controllers.Count(j => j.IsJoycon);

                // when we want to have a single joycon in vertical mode
                bool joinSelf = nbJoycons == 1 || controller.Config.DoNotRejoin;

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

        public void SetLEDByPadID(Joycon controller)
        {
            controller.HidapiLock.EnterReadLock();
            try
            {
                controller.SetLEDByPadID();
            }
            finally
            {
                controller.HidapiLock.ExitReadLock();
            }
        }

        public void SetHomeLight(Joycon controller, bool on)
        {
            controller.HidapiLock.EnterReadLock();
            try
            {
                controller.SetHomeLight(on);
            }
            finally
            {
                controller.HidapiLock.ExitReadLock();
            }
        }

        public void PowerOff(Joycon controller)
        {
            controller.HidapiLock.EnterReadLock();
            try
            {
                controller.PowerOff();
            }
            finally
            {
                controller.HidapiLock.ExitReadLock();
            }
        }

        public void ApplyConfig(Joycon controller, bool showErrors = true)
        {
            controller.HidapiLock.EnterReadLock();
            try
            {
                controller.ApplyConfig(showErrors);
            }
            finally
            {
                controller.HidapiLock.ExitReadLock();
            }
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
            catch (VigemBusNotFoundException)
            {
                _form.AppendTextBox("Could not connect to VIGEmBus. Make sure VIGEmBus driver is installed correctly.");
            }
            catch (VigemBusAccessFailedException)
            {
                _form.AppendTextBox("Could not connect to VIGEmBus. VIGEmBus is busy. Try restarting your computer or reinstalling VIGEmBus driver.");
            }
            catch (VigemBusVersionMismatchException)
            {
                _form.AppendTextBox("Could not connect to VIGEmBus. The installed VIGEmBus driver is not compatible. Install a newer version of VIGEmBus driver.");
            }
            catch (VigemAllocFailedException)
            {
                _form.AppendTextBox("Could not connect to VIGEmBus. Allocation failed. Try restarting your computer or reinstalling VIGEmBus driver.");
            }
            catch (VigemAlreadyConnectedException)
            {
                // should not happen
                _form.AppendTextBox("VIGEmBus is already connected.");
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
                _form.AppendTextBox("Motion server is OFF.");
            }
            else
            {
                Server.Start(Config.IP, Config.Port);
            }

            InputCapture.Global.RegisterEvent(GlobalKeyEvent);
            InputCapture.Global.RegisterEvent(GlobalMouseEvent);

            _form.AppendTextBox("All systems go");
            Mgr.Start();
            _isRunning = true;
        }

        private static void StartHIDHide()
        {
            if (!Config.UseHIDHide || _hidHideService.IsActive)
            {
                return;
            }

            if (!_hidHideService.IsInstalled)
            {
                _form.AppendTextBox("HIDHide is not installed.");
                return;
            }

            try
            {
                _hidHideService.IsAppListInverted = false;
            }
            catch (Exception e)
            {
                _form.AppendTextBox($"Unable to set HIDHide in whitelist mode {e.Display()}.");
                return;
            }

            //if (Config.PurgeAffectedDevices) {
            //    try {
            //        hidHideService.ClearBlockedInstancesList();
            //    } catch (Exception e) {
            //        form.AppendTextBox($"Unable to purge blacklisted devices {e.Display()}.");
            //        return;
            //    }
            //}

            try
            {
                if (Config.PurgeWhitelist)
                {
                    _hidHideService.ClearApplicationsList();
                }

                _hidHideService.AddApplicationPath(Environment.ProcessPath);
            }
            catch (Exception e)
            {
                _form.AppendTextBox($"Unable to add program to whitelist {e.Display()}.");
                return;
            }

            try
            {
                _hidHideService.IsActive = true;
            }
            catch (Exception e)
            {
                _form.AppendTextBox($"Unable to hide devices {e.Display()}.");
                return;
            }

            _form.AppendTextBox("HIDHide is enabled.");
        }

        public static void AddDeviceToBlocklist(IntPtr handle)
        {
            if (!_hidHideService.IsActive)
            {
                return;
            }

            try
            {
                var devices = new List<string>();

                var instance = HIDApi.GetInstance(handle);
                if (instance.Length == 0)
                {
                    _form.AppendTextBox("Unable to get device instance.");
                }
                else
                {
                    devices.Add(instance);
                }

                var parentInstance = HIDApi.GetParentInstance(handle);
                if (parentInstance.Length == 0)
                {
                    _form.AppendTextBox("Unable to get device parent instance.");
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
                _form.AppendTextBox($"Unable to add controller to block-list {e.Display()}.");
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
                Server.Dispose();
            }
        }

        public static void AllowAnotherInstance()
        {
            _mutexInstance?.Close();
        }

        public static void StopHIDHide()
        {
            if (!_hidHideService.IsActive)
            {
                return;
            }

            try
            {
                _hidHideService.RemoveApplicationPath(Environment.ProcessPath);
            }
            catch (Exception e)
            {
                _form.AppendTextBox($"Unable to remove program from whitelist {e.Display()}.");
            }

            if (Config.PurgeAffectedDevices)
            {
                try
                {
                    foreach (var instance in BlockedDeviceInstances)
                    {
                        _hidHideService.RemoveBlockedInstanceId(instance);
                    }
                }
                catch (Exception e)
                {
                    _form.AppendTextBox($"Unable to purge blacklisted devices  {e.Display()}.");
                }
            }

            try
            {
                _hidHideService.IsActive = false;
            }
            catch (Exception e)
            {
                _form.AppendTextBox($"Unable to disable HIDHide {e.Display()}.");
                return;
            }

            _form.AppendTextBox("HIDHide is disabled.");
        }

        public static void UpdateThirdpartyControllers(List<SController> controllers)
        {
            ThirdpartyCons.Set(controllers);
        }

        private static void Main(string[] args)
        {
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

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                _form = new MainForm();
                Application.Run(_form);
            }
        }

        private static void SetupDlls()
        {
            var archPath = $"{AppDomain.CurrentDomain.BaseDirectory}{(Environment.Is64BitProcess ? "x64" : "x86")}\\";
            var pathVariable = Environment.GetEnvironmentVariable("PATH");
            pathVariable = $"{archPath};{pathVariable}";
            Environment.SetEnvironmentVariable("PATH", pathVariable);
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
}
