using BetterJoy.Collections;
using BetterJoy.Config;
using BetterJoy.Controller;
using BetterJoy.Exceptions;
using BetterJoy.Forms;
using BetterJoy.Hardware;
using BetterJoy.Hardware.SubCommandUtils;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using WindowsInput.Events;

namespace BetterJoy;

public class Joycon
{
    public enum Button
    {
        DpadDown = 0,
        DpadRight = 1,
        DpadLeft = 2,
        DpadUp = 3,
        SL = 4,
        SR = 5,
        Minus = 6,
        Home = 7,
        Plus = 8,
        Capture = 9,
        Stick = 10,
        Shoulder1 = 11,
        Shoulder2 = 12,

        // For pro controller
        B = 13,
        A = 14,
        Y = 15,
        X = 16,
        Stick2 = 17,
        Shoulder21 = 18,
        Shoulder22 = 19
    }

    public enum ControllerType : byte
    {
        Pro = 0x01,
        JoyconLeft = 0x02,
        JoyconRight = 0x03,
        SNES = 0x04,
        N64 = 0x05,
        NES = 0x06,
        FamicomI = 0x07,
        FamicomII = 0x08,
    }

    public enum DebugType
    {
        None,
        All,
        Comms,
        Threading,
        IMU,
        Rumble,
        Shake,
        Test
    }

    public enum Status : uint
    {
        NotAttached,
        AttachError,
        Errored,
        Dropped,
        Attached,
        IMUDataOk
    }

    public enum BatteryLevel
    {
        Unknown = -1,
        Empty,
        Critical,
        Low,
        Medium,
        Full
    }

    public enum Orientation
    {
        None,
        Horizontal,
        Vertical
    }

    private enum ReceiveError
    {
        None,
        InvalidHandle,
        ReadError,
        InvalidPacket,
        NoData,
        Disconnected
    }

    private const int DeviceErroredCode = -100; // custom error

    private const int ReportLength = 49;
    private readonly int _CommandLength;
    private readonly int _MixedComsLength; // when the buffer is used for both read and write to hid

    public readonly ControllerConfig Config;

    private static readonly byte[] _ledById = [0b0001, 0b0011, 0b0111, 0b1111, 0b1001, 0b0101, 0b1101, 0b0110];

    private readonly short[] _accNeutral = [0, 0, 0];
    private readonly short[] _accRaw = [0, 0, 0];
    private readonly short[] _accSensiti = [0, 0, 0];

    private readonly MadgwickAHRS _AHRS; // for getting filtered Euler angles of rotation; 5ms sampling rate

    private readonly bool[] _buttons = new bool[20];
    private readonly bool[] _buttonsDown = new bool[20];
    private readonly long[] _buttonsDownTimestamp = new long[20];
    private readonly bool[] _buttonsUp = new bool[20];
    private readonly bool[] _buttonsPrev = new bool[20];
    private readonly bool[] _buttonsRemapped = new bool[20];

    private readonly float[] _curRotation = [0, 0, 0, 0, 0, 0]; // Filtered IMU data

    private static readonly byte[] _stopRumbleBuf = [0x0, 0x1, 0x40, 0x40, 0x0, 0x1, 0x40, 0x40]; // Stop rumble
    private readonly byte[] _rumbleBuf;

    private readonly short[] _gyrNeutral = [0, 0, 0];
    private readonly short[] _gyrRaw = [0, 0, 0];
    private readonly short[] _gyrSensiti = [0, 0, 0];

    private readonly Dictionary<int, bool> _mouseToggleBtn = [];

    // Values from https://github.com/dekuNukem/Nintendo_Switch_Reverse_Engineering/blob/master/spi_flash_notes.md#6-axis-horizontal-offsets
    private readonly short[] _accProHorOffset = [-688, 0, 4038];
    private readonly short[] _accLeftHorOffset = [350, 0, 4081];
    private readonly short[] _accRightHorOffset = [350, 0, -4081];

    private readonly Stopwatch _shakeTimer = Stopwatch.StartNew(); //Setup a timer for measuring shake in milliseconds

    private readonly byte[] _sliderVal = [0, 0];

    private readonly ushort[] _stickCal = [0, 0, 0, 0, 0, 0];
    private readonly ushort[] _stickPrecal = [0, 0];

    private readonly ushort[] _stick2Cal = [0, 0, 0, 0, 0, 0];
    private readonly ushort[] _stick2Precal = [0, 0];

    private Vector3 _accG = Vector3.Zero;
    public bool ActiveGyro;

    private bool _DumpedCalibration = false;
    private bool _IMUCalibrated = false;
    private bool _SticksCalibrated = false;
    private readonly short[] _activeIMUData = new short[6];
    private readonly ushort[] _activeStick1 = new ushort[6];
    private readonly ushort[] _activeStick2 = new ushort[6];

    public BatteryLevel Battery = BatteryLevel.Unknown;
    public bool Charging = false;

    private float _deadzone;
    private float _deadzone2;
    private float _range;
    private float _range2;

    private readonly MainForm _form;
    private readonly Logger _logger;

    private byte _globalCount;
    private Vector3 _gyrG = Vector3.Zero;

    private readonly HIDApi.Device _device;
    private bool _hasShaked;

    public readonly bool IsThirdParty;
    public readonly bool IsUSB;
    private long _lastStickDoubleClick = -1;

    public OutputControllerDualShock4 OutDs4;
    public OutputControllerXbox360 OutXbox;
    private readonly Lock _updateInputLock = new();
    private readonly Lock _ctsCommunicationsLock = new();

    public int PacketCounter;

    // For UdpServer
    public readonly int PadId;

    public PhysicalAddress PadMacAddress = new([01, 02, 03, 04, 05, 06]);
    public readonly string Path;

    private Thread _receiveReportsThread;
    private Thread _sendCommandsThread;

    private readonly RumbleQueue _rumbles;

    public readonly string SerialNumber;

    public string SerialOrMac;

    private long _shakedTime;

    private Status _state;

    public Status State
    {
        get => _state;
        private set
        {
            if (_state == value)
            {
                return;
            }

            _state = value;
            OnStateChange(new StateChangedEventArgs(value));
        }
    }

    private readonly float[] _stick = [0, 0];
    private readonly float[] _stick2 = [0, 0];

    private CancellationTokenSource _ctsCommunications;
    public ulong Timestamp { get; private set; }
    public readonly long TimestampCreation;

    private long _timestampActivity = Stopwatch.GetTimestamp();

    public ControllerType Type { get; private set; }

    public EventHandler<StateChangedEventArgs> StateChanged;

    public readonly ConcurrentList<IMUData> CalibrationIMUDatas = [];
    public readonly ConcurrentList<SticksData> CalibrationStickDatas = [];
    private bool _calibrateSticks = false;
    private bool _calibrateIMU = false;

    private readonly Stopwatch _timeSinceReceive = new();
    private readonly RollingAverage _avgReceiveDeltaMs = new(100); // delta is around 10-16ms, so rolling average over 1000-1600ms

    private volatile bool _pauseSendCommands;
    private volatile bool _sendCommandsPaused;
    private volatile bool _requestPowerOff;
    private volatile bool _requestSetLEDByPadID;

    public Joycon(
        Logger logger,
        MainForm form,
        HIDApi.Device device,
        string path,
        string serialNum,
        bool isUSB,
        int id,
        ControllerType type,
        bool isThirdParty = false
    )
    {
        _logger = logger;
        _form = form;

        Config = new(_logger);
        Config.Update();

        SerialNumber = serialNum;
        SerialOrMac = serialNum;
        _device = device;
        _rumbles = new RumbleQueue();
        _rumbleBuf = new byte[_stopRumbleBuf.Length];
        StopRumbleInSubcommands();

        for (var i = 0; i < _buttonsDownTimestamp.Length; i++)
        {
            _buttonsDownTimestamp[i] = -1;
        }

        _AHRS = new MadgwickAHRS(0.005f, Config.AHRSBeta);

        PadId = id;
        IsUSB = isUSB;
        Type = type;
        IsThirdParty = isThirdParty;
        Path = path;
        _CommandLength = isUSB ? 64 : 49;
        _MixedComsLength = Math.Max(ReportLength, _CommandLength);

        OutXbox = new OutputControllerXbox360();
        OutXbox.FeedbackReceived += ReceiveRumble;

        OutDs4 = new OutputControllerDualShock4();
        OutDs4.FeedbackReceived += Ds4_FeedbackReceived;

        TimestampCreation = new DateTimeOffset(DateTime.UtcNow).ToUnixTimeMilliseconds();
    }

    public bool IsPro => Type is ControllerType.Pro;
    public bool IsSNES => Type == ControllerType.SNES;
    public bool IsNES => Type == ControllerType.NES;
    public bool IsFamicomI => Type == ControllerType.FamicomI;
    public bool IsFamicomII => Type == ControllerType.FamicomII;
    public bool IsN64 => Type == ControllerType.N64;
    public bool IsJoycon => Type is ControllerType.JoyconRight or ControllerType.JoyconLeft;
    public bool IsLeft => Type != ControllerType.JoyconRight;
    public bool IsJoined => Other != null && Other != this;
    public bool IsPrimaryGyro => !IsJoined || Config.GyroLeftHanded == IsLeft;

    public bool IsDeviceReady => State > Status.Dropped;
    public bool IsDeviceError => !IsDeviceReady && State != Status.NotAttached;


    public Joycon Other;

    public bool SetLEDByPlayerNum(int id)
    {
        if (id >= _ledById.Length)
        {
            // No support for any higher than 8 controllers
            id = _ledById.Length - 1;
        }

        byte led = _ledById[id];

        return SetPlayerLED(led);
    }

    public bool SetLEDByPadID()
    {
        int id;
        if (!IsJoined)
        {
            // Set LED to current Pad ID
            id = PadId;
        }
        else
        {
            // Set LED to current Joycon Pair
            id = Math.Min(Other.PadId, PadId);
        }

        return SetLEDByPlayerNum(id);
    }

    public void RequestSetLEDByPadID()
    {
        _requestSetLEDByPadID = true;
    }

    public void GetActiveIMUData()
    {
        var activeIMUData = _form.ActiveCaliIMUData(SerialOrMac);

        if (activeIMUData != null)
        {
            Array.Copy(activeIMUData, _activeIMUData, 6);
            _IMUCalibrated = true;
        }
        else
        {
            _IMUCalibrated = false;
        }
    }

    public void GetActiveSticksData()
    {
        var activeSticksData = _form.ActiveCaliSticksData(SerialOrMac);
        if (activeSticksData != null)
        {
            Array.Copy(activeSticksData, _activeStick1, 6);
            Array.Copy(activeSticksData, 6, _activeStick2, 0, 6);
            _SticksCalibrated = true;
        }
        else
        {
            _SticksCalibrated = false;
        }
    }

    public void ReceiveRumble(Xbox360FeedbackReceivedEventArgs e)
    {
        if (!Config.EnableRumble)
        {
            return;
        }

        DebugPrint("Rumble data Received: XInput", DebugType.Rumble);
        SetRumble(Config.LowFreq, Config.HighFreq, e.SmallMotor / 255f, e.LargeMotor / 255f);

        if (IsJoined)
        {
            Other.SetRumble(Config.LowFreq, Config.HighFreq, e.SmallMotor / 255f, e.LargeMotor / 255f);
        }
    }

    public void Ds4_FeedbackReceived(DualShock4FeedbackReceivedEventArgs e)
    {
        if (!Config.EnableRumble)
        {
            return;
        }

        DebugPrint("Rumble data Received: DS4", DebugType.Rumble);
        SetRumble(Config.LowFreq, Config.HighFreq, e.SmallMotor / 255f, e.LargeMotor / 255f);

        if (IsJoined)
        {
            Other.SetRumble(Config.LowFreq, Config.HighFreq, e.SmallMotor / 255f, e.LargeMotor / 255f);
        }
    }

    private void OnStateChange(StateChangedEventArgs e)
    {
        StateChanged?.Invoke(this, e);
    }

    private bool ShouldLog(DebugType type)
    {
        if (Config.DebugType == DebugType.None)
        {
            return false;
        }

        return type == DebugType.All ||
               type == Config.DebugType ||
               Config.DebugType == DebugType.All;
    }
    private void DebugPrint<T>(T stringifyable, DebugType type)
    {
        if (!ShouldLog(type))
        {
            return;
        }

        Log(stringifyable.ToString(), Logger.LogLevel.Debug, type);
    }

    public Vector3 GetGyro()
    {
        return _gyrG;
    }

    public Vector3 GetAccel()
    {
        return _accG;
    }

    public bool Reset()
    {
        Log("Resetting connection.");
        return SetHCIState(0x01) > 0;
    }

    public void Attach()
    {
        if (IsDeviceReady)
        {
            return;
        }

        try
        {
            if (!_device.IsValid)
            {
                throw new DeviceNullHandleException("reset hidapi");
            }

            // Connect
            if (IsUSB)
            {
                Log("Using USB.");
                GetMAC();
                USBPairing();
                //BTManualPairing();
            }
            else
            {
                Log("Using Bluetooth.");
                GetMAC();
            }

            // set report mode to simple HID mode (fix SPI read not working when controller is already initialized)
            // do not always send a response so we don't check if there is one
            SetReportMode(InputReportMode.SimpleHID, false);

            SetLowPowerState(false);

            //Make sure we're not actually a retro controller
            if (Type == ControllerType.JoyconRight)
            {
                CheckIfRightIsRetro();
            }

            var ok = DumpCalibrationData();
            if (!ok)
            {
                throw new DeviceComFailedException("reset calibration");
            }

            BlinkHomeLight();
            SetLEDByPlayerNum(PadId);

            SetIMU(true);
            SetIMUSensitivity();

            SetRumble(true);
            SetNFCIR(false);

            SetReportMode(InputReportMode.StandardFull);

            State = Status.Attached;

            DebugPrint("Done with init.", DebugType.Comms);
        }
        catch (DeviceComFailedException)
        {
            bool resetSuccess = Reset();
            if (!resetSuccess)
            {
                State = Status.AttachError;
            }
            throw;
        }
        catch
        {
            State = Status.AttachError;
            throw;
        }
    }

    private void GetMAC()
    {
        if (IsUSB)
        {
            Span<byte> buf = stackalloc byte[ReportLength];

            // Get MAC
            if (USBCommandCheck(0x01, buf) < 10)
            {
                // can occur when USB connection isn't closed properly
                throw new DeviceComFailedException("reset mac");
            }

            PadMacAddress = new PhysicalAddress([buf[9], buf[8], buf[7], buf[6], buf[5], buf[4]]);
            SerialOrMac = PadMacAddress.ToString().ToLower();
            return;
        }

        // Serial = MAC address of the controller in bluetooth
        var mac = new byte[6];
        try
        {
            for (var n = 0; n < 6 && n < SerialNumber.Length; n++)
            {
                mac[n] = byte.Parse(SerialNumber.AsSpan(n * 2, 2), NumberStyles.HexNumber);
            }
        }
        // could not parse mac address, ignore
        catch (Exception e)
        {
            Log("Cannot parse MAC address.", e, Logger.LogLevel.Debug);
        }

        PadMacAddress = new PhysicalAddress(mac);
    }

    private void USBPairing()
    {
        // Handshake
        if (USBCommandCheck(0x02) == 0)
        {
            throw new DeviceComFailedException("reset handshake");
        }

        // 3Mbit baud rate
        if (USBCommandCheck(0x03) == 0)
        {
            throw new DeviceComFailedException("reset baud rate");
        }

        // Handshake at new baud rate
        if (USBCommandCheck(0x02) == 0)
        {
            throw new DeviceComFailedException("reset new handshake");
        }

        // Prevent HID timeout
        if (!USBCommand(0x04)) // does not send a response
        {
            throw new DeviceComFailedException("reset new hid timeout");
        }
    }

    private void BTManualPairing()
    {
        Span<byte> buf = stackalloc byte[ReportLength];

        // Bluetooth manual pairing
        byte[] btmac_host = Program.BtMac.GetAddressBytes();

        // send host MAC and acquire Joycon MAC
        SubcommandCheck(SubCommandOperation.ManualBluetoothPairing, [0x01, btmac_host[5], btmac_host[4], btmac_host[3], btmac_host[2], btmac_host[1], btmac_host[0]], buf);
        SubcommandCheck(SubCommandOperation.ManualBluetoothPairing, [0x02], buf); // LTKhash
        SubcommandCheck(SubCommandOperation.ManualBluetoothPairing, [0x03], buf); // save pairing info
    }

    public bool SetPlayerLED(byte leds = 0x00)
    {
        return SubcommandCheck(SubCommandOperation.SetPlayerLights, [leds]) > 0;
    }

    // Do not call after initial setup
    public void BlinkHomeLight()
    {
        if (!HomeLightSupported())
        {
            return;
        }

        const byte Intensity = 0x1;

        Span<byte> buf =
        [
            // Global settings
            0x18,
            0x01,

            // Mini cycle 1
            BitWrangler.LowerToUpper(Intensity),
            0xFF,
            0xFF,
        ];
        SubcommandCheck(SubCommandOperation.SetHomeLight, buf);
    }

    public bool SetHomeLight(bool on)
    {
        if (!HomeLightSupported())
        {
            return false;
        }

        var intensity = (byte)(on ? 0x1 : 0x0);
        const byte NbCycles = 0xF; // 0x0 for permanent light

        Span<byte> buf =
        [
            // Global settings
            0x0F, // 0XF = 175ms base duration
            BitWrangler.EncodeNibblesAsByteLittleEndian(NbCycles, intensity),

            // Mini cycle 1
            // Somehow still used when buf[0] high nibble is set to 0x0
            // Increase the multipliers (like 0xFF instead of 0x11) to increase the duration beyond 2625ms
            BitWrangler.LowerToUpper(intensity), // intensity | not used
            0x11, // transition multiplier | duration multiplier, both use the base duration
            0xFF, // not used
        ];

        Subcommand(SubCommandOperation.SetHomeLight, buf); // don't wait for response

        return true;
    }

    private int SetHCIState(byte state)
    {
        StopRumbleInSubcommands();
        return SubcommandCheck(SubCommandOperation.SetHCIState, [state]);
    }

    private void SetIMU(bool enable)
    {
        if (!IMUSupported())
        {
            return;
        }

        SubcommandCheck(SubCommandOperation.EnableIMU, [enable ? (byte)0x01 : (byte)0x00]);
    }

    private void SetIMUSensitivity()
    {
        if (!IMUSupported())
        {
            return;
        }

        Span<byte> buf =
        [
            0x03, // gyroscope sensitivity : 0x00 = 250dps, 0x01 = 500dps, 0x02 = 1000dps, 0x03 = 2000dps (default)
            0x00, // accelerometer sensitivity : 0x00 = 8G (default), 0x01 = 4G, 0x02 = 2G, 0x03 = 16G
            0x01, // gyroscope performance rate : 0x00 = 833hz, 0x01 = 208hz (default)
            0x01  // accelerometer anti-aliasing filter bandwidth : 0x00 = 200hz, 0x01 = 100hz (default)
        ];
        SubcommandCheck(SubCommandOperation.SetIMUSensitivity, buf);
    }

    private void SetRumble(bool enable)
    {
        SubcommandCheck(SubCommandOperation.EnableVibration, [enable ? (byte)0x01 : (byte)0x00]);
    }

    private void IgnoreRumbleInSubcommands()
    {
        Array.Clear(_rumbleBuf);
    }

    private void StopRumbleInSubcommands()
    {
        Array.Copy(_stopRumbleBuf, _rumbleBuf, _rumbleBuf.Length);
    }

    private void SetNFCIR(bool enable)
    {
        if (Type != ControllerType.JoyconRight)
        {
            return;
        }

        SubcommandCheck(SubCommandOperation.SetMCUState, [enable ? (byte)0x01 : (byte)0x00]);
    }

    private bool SetReportMode(InputReportMode reportMode, bool checkResponse = true)
    {
        if (checkResponse)
        {
            return SubcommandCheck(SubCommandOperation.SetReportMode, [(byte)reportMode]) > 0;
        }
        Subcommand(SubCommandOperation.SetReportMode, [(byte)reportMode]);
        return true;
    }

    private void CheckIfRightIsRetro()
    {
        Span<byte> response = stackalloc byte[ReportLength];

        for (var i = 0; i < 5; ++i)
        {
            var respLength = SubcommandCheck(SubCommandOperation.RequestDeviceInfo, [], response, false);

            if (respLength > 0)
            {
                // The NES and Famicom controllers both share the hardware id of a normal right joycon.
                // To identify them, we need to query the hardware directly.
                // NES Left: 0x09
                // NES Right: 0x0A
                // Famicom I (Left): 0x07
                // Famicom II (Right): 0x08
                var deviceType = response[17];

                switch (deviceType)
                {
                    case 0x02:
                        // Do nothing, it's the right joycon
                        break;
                    case 0x09:
                    case 0x0A:
                        Type = ControllerType.NES;
                        break;
                    case 0x07:
                        Type = ControllerType.FamicomI;
                        break;
                    case 0x08:
                        Type = ControllerType.FamicomII;
                        break;
                    default:
                        Log($"Unknown device type: {deviceType:X2}", Logger.LogLevel.Warning);
                        break;
                }

                return;
            }
        }

        throw new DeviceComFailedException("reset device info");
    }

    private void SetLowPowerState(bool enable)
    {
        SubcommandCheck(SubCommandOperation.EnableLowPowerMode, [enable ? (byte)0x01 : (byte)0x00]);
    }

    private void BTActivate()
    {
        if (!IsUSB)
        {
            return;
        }

        // Allow device to talk to BT again
        USBCommand(0x05);
        USBCommand(0x06);
    }

    public bool PowerOff()
    {
        if (IsDeviceReady)
        {
            Log("Powering off.");

            // < 0 = error = we assume it's powered off, ideally should check for 0x0000048F (device not connected) error in hidapi
            var length = SetHCIState(0x00);
            if (length != 0)
            {
                Drop(false, false);
                return true;
            }
        }

        return false;
    }

    public void RequestPowerOff()
    {
        _requestPowerOff = true;
    }

    public void WaitPowerOff(int timeoutMs)
    {
        _receiveReportsThread?.Join(timeoutMs);
    }

    private void BatteryChanged()
    {
        // battery changed level
        _form.SetBatteryColor(this, Battery);

        if (!IsUSB && !Charging && Battery <= BatteryLevel.Critical)
        {
            var msg = $"Controller {PadId} ({GetControllerName()}) - low battery notification!";
            _form.Tooltip(msg);
        }
    }

    private void ChargingChanged()
    {
        _form.SetCharging(this, Charging);
    }

    private static bool Retry(Func<bool> func, int waitMs = 500, int nbAttempt = 3)
    {
        bool success = false;

        for (int attempt = 0; attempt < nbAttempt && !success; ++attempt)
        {
            if (attempt > 0)
            {
                Thread.Sleep(waitMs);
            }

            success = func();
        }

        return success;
    }

    public void Detach(bool close = true)
    {
        if (State == Status.NotAttached)
        {
            return;
        }

        AbortCommunicationThreads();
        DisconnectViGEm();
        StopRumbleInSubcommands();
        _rumbles.Clear();

        if (_device.IsValid)
        {
            if (IsDeviceReady)
            {
                //SetIMU(false);
                //SetRumble(false);
                var sent = Retry(() => SetReportMode(InputReportMode.SimpleHID));
                if (sent)
                {
                    Retry(() => SetPlayerLED(0));
                }

                // Commented because you need to restart the controller to reconnect in usb again with the following
                //BTActivate();
            }

            if (close)
            {
                _device.Dispose();
            }
        }

        State = Status.NotAttached;
    }

    public void Drop(bool error = false, bool waitThreads = true)
    {
        // when waitThreads is false, doesn't dispose the cancellation token
        // so you have to call AbortCommunicationThreads again with waitThreads to true
        AbortCommunicationThreads(waitThreads);

        State = error ? Status.Errored : Status.Dropped;
    }

    private void AbortCommunicationThreads(bool waitThreads = true)
    {
        lock (_ctsCommunicationsLock)
        {
            if (_ctsCommunications != null && !_ctsCommunications.IsCancellationRequested)
            {
                _ctsCommunications.Cancel();
            }
        }

        if (waitThreads)
        {
            _receiveReportsThread?.Join();
            _sendCommandsThread?.Join();

            lock (_ctsCommunicationsLock)
            {
                if (_ctsCommunications != null)
                {
                    _ctsCommunications.Dispose();
                    _ctsCommunications = null;
                }
            }
        }
    }

    public bool IsViGEmSetup()
    {
        return (!Config.ShowAsXInput || OutXbox.IsConnected()) && (!Config.ShowAsDs4 || OutDs4.IsConnected());
    }

    public void ConnectViGEm()
    {
        if (Config.ShowAsXInput)
        {
            DebugPrint("Connect virtual xbox controller.", DebugType.Comms);
            OutXbox.Connect();
        }

        if (Config.ShowAsDs4)
        {
            DebugPrint("Connect virtual DS4 controller.", DebugType.Comms);
            OutDs4.Connect();
        }
    }

    public void DisconnectViGEm()
    {
        OutXbox.Disconnect();
        OutDs4.Disconnect();
    }

    private void UpdateInput()
    {
        try
        {
            OutDs4.UpdateInput(MapToDualShock4Input(this));
            OutXbox.UpdateInput(MapToXbox360Input(this));
        }
        // ignore
        catch (Exception e)
        {
            Log("Cannot update input.", e, Logger.LogLevel.Debug);
        }
    }

    // Run from poll thread
    private ReceiveError ReceiveRaw(Span<byte> buf)
    {
        if (!_device.IsValid)
        {
            return ReceiveError.InvalidHandle;
        }

        // The controller should report back at 60hz or between 60-120hz for the Pro Controller in USB
        var length = Read(buf, 100);

        if (length < 0)
        {
            return ReceiveError.ReadError;
        }

        if (length == 0)
        {
            return ReceiveError.NoData;
        }

        //DebugPrint($"Received packet {buf[0]:X}", DebugType.Threading);

        byte packetType = buf[0];

        if (packetType == (byte)InputReportMode.USBHID &&
            length > 2 &&
            buf[1] == 0x01 && buf[2] == 0x03)
        {
            return ReceiveError.Disconnected;
        }

        if (packetType != (byte)InputReportMode.StandardFull && packetType != (byte)InputReportMode.SimpleHID)
        {
            return ReceiveError.InvalidPacket;
        }

        // Clear remaining of buffer just to be safe
        if (length < ReportLength)
        {
            buf[length..ReportLength].Clear();
        }

        //DebugPrint($"Bytes read: {length:D}. Elapsed: {deltaReceiveMs}ms AVG: {_avgReceiveDeltaMs.GetAverage()}ms", DebugType.Threading);

        return ReceiveError.None;
    }

    private void ProcessInputReport(ReadOnlySpan<byte> buf)
    {
        const int NbIMUPackets = 3;

        ulong deltaPacketsMicroseconds = 0;
        byte packetType = buf[0];

        if (packetType == (byte)InputReportMode.StandardFull)
        {
            // Determine the IMU timestamp with a rolling average instead of relying on the unreliable packet's timestamp
            // more detailed explanations on why : https://github.com/torvalds/linux/blob/52b1853b080a082ec3749c3a9577f6c71b1d4a90/drivers/hid/hid-nintendo.c#L1115
            if (_timeSinceReceive.IsRunning)
            {
                var deltaReceiveMs = _timeSinceReceive.ElapsedMilliseconds;
                _avgReceiveDeltaMs.AddValue((int)deltaReceiveMs);
            }
            _timeSinceReceive.Restart();

            var deltaPacketsMs = _avgReceiveDeltaMs.GetAverage() / NbIMUPackets;
            deltaPacketsMicroseconds = (ulong)(deltaPacketsMs * 1000);

            _AHRS.SamplePeriod = deltaPacketsMs / 1000;
        }

        var mainController = this;

        try
        {
            // Only joycons support joining. Need to lock to synchronize inputs between two joycons
            if (Type == ControllerType.JoyconLeft) 
            {
                _updateInputLock.Enter();
            }
            else if (!IsLeft && IsJoined)
            {
                mainController = Other;
                mainController._updateInputLock.Enter();
            }

            GetBatteryInfos(buf);
            ProcessButtonsAndSticks(buf);
            CopyInputFromJoinedController();
            UpdateInputActivity();

            // Process packets as soon as they come
            for (var n = 0; n < NbIMUPackets; n++)
            {
                bool updateIMU = ExtractIMUValues(buf, n);
                if (!updateIMU)
                {
                    break;
                }

                DoThingsWithIMU();

                Timestamp += deltaPacketsMicroseconds;
                PacketCounter++;

                Program.Server?.NewReportIncoming(this);
            }

            DoThingsWithButtons();

            mainController.UpdateInput();
        }
        finally
        {
            if (mainController._updateInputLock.IsHeldByCurrentThread)
            {
                mainController._updateInputLock.Exit();
            }
        }
    }

    private void DetectShake()
    {
        if (!Config.ShakeInputEnabled || !IsPrimaryGyro)
        {
            _hasShaked = false;
            return;
        }

        var currentShakeTime = _shakeTimer.ElapsedMilliseconds;

        // If controller was shaked then release mapped key after a small delay to simulate a button press, then reset hasShaked
        if (_hasShaked && currentShakeTime >= _shakedTime + 10)
        {
            _hasShaked = false;

            // Mapped shake key up
            Simulate(Settings.Value("shake"), false, true);
            DebugPrint("Shake completed", DebugType.Shake);
        }

        if (!_hasShaked)
        {
            // Shake detection logic
            var isShaking = GetAccel().LengthSquared() >= Config.ShakeSensitivity;
            if (isShaking && (currentShakeTime >= _shakedTime + Config.ShakeDelay || _shakedTime == 0))
            {
                _shakedTime = currentShakeTime;
                _hasShaked = true;

                // Mapped shake key down
                Simulate(Settings.Value("shake"), false);
                DebugPrint($"Shaked at time: {_shakedTime}", DebugType.Shake);
            }
        }
    }

    private void Simulate(string s, bool click = true, bool up = false)
    {
        if (s.StartsWith("key_"))
        {
            var key = (KeyCode)int.Parse(s.AsSpan(4));

            if (click)
            {
                WindowsInput.Simulate.Events().Click(key).Invoke();
            }
            else
            {
                if (up)
                {
                    WindowsInput.Simulate.Events().Release(key).Invoke();
                }
                else
                {
                    WindowsInput.Simulate.Events().Hold(key).Invoke();
                }
            }
        }
        else if (s.StartsWith("mse_"))
        {
            var button = (ButtonCode)int.Parse(s.AsSpan(4));

            if (click)
            {
                WindowsInput.Simulate.Events().Click(button).Invoke();
            }
            else
            {
                if (Config.DragToggle)
                {
                    if (!up)
                    {
                        _mouseToggleBtn.TryGetValue((int)button, out var release);

                        if (release)
                        {
                            WindowsInput.Simulate.Events().Release(button).Invoke();
                        }
                        else
                        {
                            WindowsInput.Simulate.Events().Hold(button).Invoke();
                        }

                        _mouseToggleBtn[(int)button] = !release;
                    }
                }
                else
                {
                    if (up)
                    {
                        WindowsInput.Simulate.Events().Release(button).Invoke();
                    }
                    else
                    {
                        WindowsInput.Simulate.Events().Hold(button).Invoke();
                    }
                }
            }
        }
    }

    // For Joystick->Joystick inputs
    private void SimulateContinous(int origin, string s)
    {
        SimulateContinous(_buttons[origin], s);
    }

    private void SimulateContinous(bool pressed, string s)
    {
        if (s.StartsWith("joy_"))
        {
            var button = int.Parse(s.AsSpan(4));
            _buttonsRemapped[button] |= pressed;
        }
    }

    private void ReleaseRemappedButtons()
    {
        // overwrite custom-mapped buttons
        if (Settings.Value("capture") != "0")
        {
            _buttonsRemapped[(int)Button.Capture] = false;
        }

        if (Settings.Value("home") != "0")
        {
            _buttonsRemapped[(int)Button.Home] = false;
        }

        // single joycon mode
        if (IsLeft)
        {
            if (Settings.Value("sl_l") != "0")
            {
                _buttonsRemapped[(int)Button.SL] = false;
            }

            if (Settings.Value("sr_l") != "0")
            {
                _buttonsRemapped[(int)Button.SR] = false;
            }
        }
        else
        {
            if (Settings.Value("sl_r") != "0")
            {
                _buttonsRemapped[(int)Button.SL] = false;
            }

            if (Settings.Value("sr_r") != "0")
            {
                _buttonsRemapped[(int)Button.SR] = false;
            }
        }
    }

    private void SimulateRemappedButtons()
    {
        if (_buttonsDown[(int)Button.Capture])
        {
            Simulate(Settings.Value("capture"), false);
        }

        if (_buttonsUp[(int)Button.Capture])
        {
            Simulate(Settings.Value("capture"), false, true);
        }

        if (_buttonsDown[(int)Button.Home])
        {
            Simulate(Settings.Value("home"), false);
        }

        if (_buttonsUp[(int)Button.Home])
        {
            Simulate(Settings.Value("home"), false, true);
        }

        SimulateContinous((int)Button.Capture, Settings.Value("capture"));
        SimulateContinous((int)Button.Home, Settings.Value("home"));

        if (IsLeft)
        {
            if (_buttonsDown[(int)Button.SL])
            {
                Simulate(Settings.Value("sl_l"), false);
            }

            if (_buttonsUp[(int)Button.SL])
            {
                Simulate(Settings.Value("sl_l"), false, true);
            }

            if (_buttonsDown[(int)Button.SR])
            {
                Simulate(Settings.Value("sr_l"), false);
            }

            if (_buttonsUp[(int)Button.SR])
            {
                Simulate(Settings.Value("sr_l"), false, true);
            }

            SimulateContinous(_buttons[(int)Button.SL], Settings.Value("sl_l"));
            SimulateContinous(_buttons[(int)Button.SR], Settings.Value("sr_l"));
        }

        if (!IsLeft || IsJoined)
        {
            var controller = !IsLeft ? this : Other;

            if (controller._buttonsDown[(int)Button.SL])
            {
                Simulate(Settings.Value("sl_r"), false);
            }

            if (controller._buttonsUp[(int)Button.SL])
            {
                Simulate(Settings.Value("sl_r"), false, true);
            }

            if (controller._buttonsDown[(int)Button.SR])
            {
                Simulate(Settings.Value("sr_r"), false);
            }

            if (controller._buttonsUp[(int)Button.SR])
            {
                Simulate(Settings.Value("sr_r"), false, true);
            }

            SimulateContinous(controller._buttons[(int)Button.SL], Settings.Value("sl_r"));
            SimulateContinous(controller._buttons[(int)Button.SR], Settings.Value("sr_r"));
        }

        bool hasShaked = IsPrimaryGyro ? _hasShaked : Other._hasShaked;
        SimulateContinous(hasShaked, Settings.Value("shake"));
    }

    private void RemapButtons()
    {
        Array.Copy(_buttons, _buttonsRemapped, _buttons.Length);

        ReleaseRemappedButtons();
        SimulateRemappedButtons();
    }

    private static bool HandleJoyAction(string settingKey, out int button)
    {
        var resVal = Settings.Value(settingKey);
        if (resVal.StartsWith("joy_") && int.TryParse(resVal.AsSpan(4), out button))
        {
            return true;
        }

        button = 0;
        return false;
    }

    private bool IsButtonDown(int button)
    {
        return _buttonsDown[button] || (Other != null && Other._buttonsDown[button]);
    }

    private bool IsButtonUp(int button)
    {
        return _buttonsUp[button] || (Other != null && Other._buttonsUp[button]);
    }

    private Joycon GetMainController()
    {
        return IsLeft || !IsJoined ? this : Other;
    }

    // Must be done by the main controller (in the case they are joined)
    private void DoThingsWithButtonsMainController()
    {
        var powerOffButton = (int)(!IsJoycon || !IsLeft || IsJoined ? Button.Home : Button.Capture);
        var timestampNow = Stopwatch.GetTimestamp();

        if (!IsUSB)
        {
            bool powerOff = false;

            if (Config.HomeLongPowerOff && _buttons[powerOffButton])
            {
                var powerOffPressedDurationMs = TimestampToMs(timestampNow - _buttonsDownTimestamp[powerOffButton]);
                if (powerOffPressedDurationMs > 2000)
                {
                    powerOff = true;
                }
            }

            if (Config.PowerOffInactivityMins > 0)
            {
                var timeSinceActivityMs = TimestampToMs(timestampNow - _timestampActivity);
                if (timeSinceActivityMs > Config.PowerOffInactivityMins * 60 * 1000)
                {
                    powerOff = true;
                }
            }

            if (powerOff)
            {
                if (IsJoined)
                {
                    Other.RequestPowerOff();
                }

                RequestPowerOff();
            }
        }

        RemapButtons();
    }

    // Must be done by all controllers when any button is updated (in the case they are joined)
    private void DoThingsWithButtonsEachController()
    {
        if (Config.ChangeOrientationDoubleClick && IsJoycon && !_calibrateSticks && !_calibrateIMU)
        {
            const int MaxClickDelayMs = 300;

            if (_buttonsDown[(int)Button.Stick])
            {
                if (_lastStickDoubleClick != -1 && 
                    TimestampToMs(_buttonsDownTimestamp[(int)Button.Stick] - _lastStickDoubleClick) < MaxClickDelayMs)
                {
                    Program.Mgr.JoinOrSplitJoycon(this);
                    _lastStickDoubleClick = -1;
                }
                else
                {
                    _lastStickDoubleClick = _buttonsDownTimestamp[(int)Button.Stick];
                }
            }
        }

        if (HandleJoyAction("swap_ab", out int button) && IsButtonDown(button))
        {
            Config.SwapAB = !Config.SwapAB;
        }

        if (HandleJoyAction("swap_xy", out button) && IsButtonDown(button))
        {
            Config.SwapXY = !Config.SwapXY;
        }

        if (HandleJoyAction("active_gyro", out button))
        {
            if (Config.GyroHoldToggle)
            {
                if (IsButtonDown(button))
                {
                    ActiveGyro = true;
                }
                else if (IsButtonUp(button))
                {
                    ActiveGyro = false;
                }
            }
            else
            {
                if (IsButtonDown(button))
                {
                    ActiveGyro = !ActiveGyro;
                }
            }
        }

        if (IsPrimaryGyro && Config.ExtraGyroFeature == "mouse")
        {
            // reset mouse position to centre of primary monitor
            if (HandleJoyAction("reset_mouse", out button) && IsButtonDown(button))
            {
                WindowsInput.Simulate.Events()
                    .MoveTo(
                        Screen.PrimaryScreen.Bounds.Width / 2,
                        Screen.PrimaryScreen.Bounds.Height / 2
                    )
                    .Invoke();
            }
        }
    }

    // Must be done by the main controller (in the case they are joined)
    private void DoThingsWithIMUMainController()
    {
        if (UseGyroAnalogSliders())
        {
            var leftT = IsLeft ? Button.Shoulder2 : Button.Shoulder22;
            var rightT = IsLeft ? Button.Shoulder22 : Button.Shoulder2;
            var left = IsLeft || !IsJoycon ? this : Other;
            var right = !IsLeft || !IsJoycon ? this : Other;
            float leftDt = left._AHRS.SamplePeriod;
            float rightDt = right._AHRS.SamplePeriod;

            int ldy, rdy;
            if (Config.UseFilteredIMU)
            {
                ldy = (int)(Config.GyroAnalogSensitivity * (left._curRotation[0] - left._curRotation[3]));
                rdy = (int)(Config.GyroAnalogSensitivity * (right._curRotation[0] - right._curRotation[3]));
            }
            else
            {
                ldy = (int)(Config.GyroAnalogSensitivity * (left._gyrG.Y * leftDt));
                rdy = (int)(Config.GyroAnalogSensitivity * (right._gyrG.Y * rightDt));
            }

            if (_buttons[(int)leftT])
            {
                _sliderVal[0] = (byte)Math.Clamp(_sliderVal[0] + ldy, 0, byte.MaxValue);
            }
            else
            {
                _sliderVal[0] = 0;
            }

            if (_buttons[(int)rightT])
            {
                _sliderVal[1] = (byte)Math.Clamp(_sliderVal[1] + rdy, 0, byte.MaxValue);
            }
            else
            {
                _sliderVal[1] = 0;
            }
        }
    }

    // Must be done by all controllers when their IMU is updated (in the case they are joined)
    private void DoThingsWithIMUEachController()
    {
        // Filtered IMU data
        _AHRS.GetEulerAngles(_curRotation);

        DetectShake();

        if (IsPrimaryGyro)
        {
            float dt = _AHRS.SamplePeriod;

            if (Config.ExtraGyroFeature.StartsWith("joy"))
            {
                if (Settings.Value("active_gyro") == "0" || ActiveGyro)
                {
                    var mainController = GetMainController();
                    var controlStick = Config.ExtraGyroFeature == "joy_left" ? mainController._stick : mainController._stick2;

                    float dx, dy;
                    if (Config.UseFilteredIMU)
                    {
                        dx = Config.GyroStickSensitivity[0] * (_curRotation[1] - _curRotation[4]); // yaw
                        dy = -(Config.GyroStickSensitivity[1] * (_curRotation[0] - _curRotation[3])); // pitch
                    }
                    else
                    {
                        dx = Config.GyroStickSensitivity[0] * (_gyrG.Z * dt); // yaw
                        dy = -(Config.GyroStickSensitivity[1] * (_gyrG.Y * dt)); // pitch
                    }

                    controlStick[0] = Math.Clamp(controlStick[0] / Config.GyroStickReduction + dx, -1.0f, 1.0f);
                    controlStick[1] = Math.Clamp(controlStick[1] / Config.GyroStickReduction + dy, -1.0f, 1.0f);
                }
            }
            else if (Config.ExtraGyroFeature == "mouse")
            {
                // gyro data is in degrees/s
                if (Settings.Value("active_gyro") == "0" || ActiveGyro)
                {
                    int dx, dy;

                    if (Config.UseFilteredIMU)
                    {
                        dx = (int)(Config.GyroMouseSensitivity[0] * (_curRotation[1] - _curRotation[4])); // yaw
                        dy = (int)-(Config.GyroMouseSensitivity[1] * (_curRotation[0] - _curRotation[3])); // pitch
                    }
                    else
                    {
                        dx = (int)(Config.GyroMouseSensitivity[0] * (_gyrG.Z * dt));
                        dy = (int)-(Config.GyroMouseSensitivity[1] * (_gyrG.Y * dt));
                    }

                    WindowsInput.Simulate.Events().MoveBy(dx, dy).Invoke();
                }
            }
        }
    }

    private void DoThingsWithButtons()
    {
        // Updating a controller's button impacts the joined controller
        DoThingsWithButtonsEachController();
        if (IsJoined)
        {
            Other.DoThingsWithButtonsEachController();
        }

        var mainController = GetMainController();
        mainController.DoThingsWithButtonsMainController();
    }

    private void DoThingsWithIMU()
    {
        DoThingsWithIMUEachController();

        var mainController = GetMainController();
        mainController.DoThingsWithIMUMainController();
    }

    private void GetBatteryInfos(ReadOnlySpan<byte> reportBuf)
    {
        byte packetType = reportBuf[0];
        if (packetType != (byte)InputReportMode.StandardFull)
        {
            return;
        }

        var prevBattery = Battery;
        var prevCharging = Charging;

        byte highNibble = (byte)(reportBuf[2] >> 4);
        Battery = (BatteryLevel)Math.Clamp(highNibble >> 1, (byte)BatteryLevel.Empty, (byte)BatteryLevel.Full);
        Charging = (highNibble & 0x1) == 1;

        if (prevBattery != Battery)
        {
            BatteryChanged();
        }

        if (prevCharging != Charging)
        {
            ChargingChanged();
        }
    }

    private void SendCommands(CancellationToken token)
    {
        // the home light stays on for 2625ms, set to less than half in case of packet drop
        const int SendHomeLightIntervalMs = 1250;
        Stopwatch timeSinceHomeLight = new();
        var oldHomeLEDOn = false;

        while (IsDeviceReady)
        {
            token.ThrowIfCancellationRequested();

            if (_pauseSendCommands || Program.IsSuspended)
            {
                if (!_sendCommandsPaused)
                {
                    StopRumbleInSubcommands();
                    _sendCommandsPaused = true;
                }

                Thread.Sleep(10);
                continue;
            }

            _sendCommandsPaused = false;

            var sendRumble = _rumbles.TryDequeue(_rumbleBuf);

            var subCommandSent = false;
            var homeLEDOn = Config.HomeLEDOn;

            if ((oldHomeLEDOn != homeLEDOn) ||
                (homeLEDOn && timeSinceHomeLight.ElapsedMilliseconds > SendHomeLightIntervalMs))
            {
                subCommandSent = SetHomeLight(true);
                timeSinceHomeLight.Restart();
                oldHomeLEDOn = homeLEDOn;
            }

            if (sendRumble)
            {
                // Subcommands send the rumble so no need to call SetRumble
                if (!subCommandSent)
                {
                    SetRumble(true);
                }

                IgnoreRumbleInSubcommands();
            }

            Thread.Sleep(5);
        }
    }

    private void ReceiveReports(CancellationToken token)
    {
        Span<byte> buf = stackalloc byte[ReportLength];
        buf.Clear();

        int dropAfterMs = IsUSB ? 1500 : 3000;
        Stopwatch timeSinceError = new();
        Stopwatch timeSinceRequest = new();
        int reconnectAttempts = 0;

        // For IMU timestamp calculation
        _avgReceiveDeltaMs.Clear();
        _avgReceiveDeltaMs.AddValue(15); // default value of 15ms between packets
        _timeSinceReceive.Reset();
        Timestamp = 0;

        while (IsDeviceReady)
        {
            token.ThrowIfCancellationRequested();

            if (Program.IsSuspended)
            {
                Thread.Sleep(10);
                continue;
            }

            // Requests here since we need to read and write, otherwise not thread safe
            bool requestPowerOff = _requestPowerOff;
            bool requestSetLEDByPadID = _requestSetLEDByPadID;

            if (requestPowerOff || requestSetLEDByPadID)
            {
                if (!timeSinceRequest.IsRunning || timeSinceRequest.ElapsedMilliseconds > 500)
                {
                    _pauseSendCommands = true;
                    if (!_sendCommandsPaused)
                    {
                        Thread.Sleep(10);
                        continue;
                    }

                    bool requestSuccess = false;

                    if (requestPowerOff)
                    {
                        requestSuccess = PowerOff();
                        DebugPrint($"Request PowerOff: ok={requestSuccess}", DebugType.Comms);

                        if (requestSuccess)
                        {
                            // exit
                            continue;
                        }
                    }
                    else if (requestSetLEDByPadID)
                    {
                        requestSuccess = SetLEDByPadID();
                        DebugPrint($"Request SetLEDByPadID: ok={requestSuccess}", DebugType.Comms);

                        if (requestSuccess)
                        {
                            _requestSetLEDByPadID = false;
                        }
                    }

                    if (requestSuccess)
                    {
                        timeSinceRequest.Reset();
                    }
                    else
                    {
                        timeSinceRequest.Restart();
                    }
                }
            }

            // Attempt reconnection, we interrupt the thread send commands to improve the reliability
            // and to avoid thread safety issues with hidapi as we're doing both read/write
            if (timeSinceError.ElapsedMilliseconds > dropAfterMs)
            {
                if (requestPowerOff || (IsUSB && reconnectAttempts >= 3))
                {
                    Log("Dropped.", Logger.LogLevel.Warning);
                    Drop(!requestPowerOff, false);

                    // exit
                    continue;
                }

                _pauseSendCommands = true;
                if (!_sendCommandsPaused)
                {
                    Thread.Sleep(10);
                    continue;
                }

                if (IsUSB)
                {
                    Log("Attempt soft reconnect...");
                    try
                    {
                        USBPairing();
                        SetReportMode(InputReportMode.StandardFull);
                        RequestSetLEDByPadID();
                    }
                    // ignore and retry
                    catch (Exception e)
                    {
                        Log("Soft reconnect failed.", e, Logger.LogLevel.Debug);
                    }
                }
                else
                {
                    //Log("Attempt soft reconnect...");
                    SetReportMode(InputReportMode.StandardFull);
                }

                ++reconnectAttempts;
                timeSinceError.Restart();
            }

            // Receive controller data
            var error = ReceiveRaw(buf);

            switch (error)
            {
                case ReceiveError.None:
                    ProcessInputReport(buf);

                    if (IsDeviceReady)
                    {
                        State = Status.IMUDataOk;
                        timeSinceError.Reset();
                        reconnectAttempts = 0;
                        _pauseSendCommands = false;
                    }
                    break;
                case ReceiveError.InvalidHandle:
                    // should not happen
                    Log("Dropped (invalid handle).", Logger.LogLevel.Error);
                    Drop(true, false);
                    break;
                case ReceiveError.Disconnected:
                    Log("Disconnected.", Logger.LogLevel.Warning);
                    Drop(true, false);
                    break;
                default:
                    timeSinceError.Start();

                    // No data read, read error or invalid packet
                    if (error == ReceiveError.ReadError)
                    {
                        Thread.Sleep(5); // to avoid spin
                    }
                    break;
            }
        }
    }

    private static ushort Scale16bitsTo12bits(ushort  value)
    {
        const float Scale16bitsTo12bits = 4095f / 65535f;

        return (ushort)MathF.Round(value * Scale16bitsTo12bits);
    }

    private void ExtractSticksValues(ReadOnlySpan<byte> reportBuf)
    {
        if (!SticksSupported())
        {
            return;
        }

        byte reportType = reportBuf[0];

        if (reportType == (byte)InputReportMode.StandardFull)
        {
            var offset = IsLeft ? 0 : 3;

            _stickPrecal[0] = BitWrangler.Lower3NibblesLittleEndian(reportBuf[6 + offset], reportBuf[7 + offset]);
            _stickPrecal[1] = BitWrangler.Upper3NibblesLittleEndian(reportBuf[7 + offset], reportBuf[8 + offset]);

            if (IsPro)
            {
                _stick2Precal[0] = BitWrangler.Lower3NibblesLittleEndian(reportBuf[9], reportBuf[10]);
                _stick2Precal[1] = BitWrangler.Upper3NibblesLittleEndian(reportBuf[10], reportBuf[11]);
            }
        }
        else if (reportType == (byte)InputReportMode.SimpleHID)
        {
            if (IsPro)
            {
                // Scale down to 12 bits to match the calibrations datas precision
                // Invert y axis by substracting from 0xFFFF to match 0x30 reports 
                _stickPrecal[0] = Scale16bitsTo12bits(BitWrangler.EncodeBytesAsWordLittleEndian(reportBuf[4], reportBuf[5]));
                _stickPrecal[1] = Scale16bitsTo12bits(BitWrangler.InvertWord(BitWrangler.EncodeBytesAsWordLittleEndian(reportBuf[6], reportBuf[7])));

                _stick2Precal[0] = Scale16bitsTo12bits(BitWrangler.EncodeBytesAsWordLittleEndian(reportBuf[8], reportBuf[9]));
                _stick2Precal[1] = Scale16bitsTo12bits(BitWrangler.InvertWord(BitWrangler.EncodeBytesAsWordLittleEndian(reportBuf[10], (reportBuf[11]))));
            }
            else
            {
                // Simulate stick data from stick hat data

                int offsetX = 0;
                int offsetY = 0;

                byte stickHat = reportBuf[3];

                // Rotate the stick hat to the correct stick orientation.
                // The following table contains the position of the stick hat for each value
                // Each value on the edges can be easily rotated with a modulo as those are successive increments of 2
                // (1 3 5 7) and (0 2 4 6)
                // ------------------
                // | SL | SYNC | SR |
                // |----------------|
                // | 7  |  0   | 1  |
                // |----------------|
                // | 6  |  8   | 2  |
                // |----------------|
                // | 5  |  4   | 3  |
                // ------------------
                if (stickHat < 0x08) // Some thirdparty controller set it to 0x0F instead of 0x08 when centered
                {
                    var rotation = IsLeft ? 0x02 : 0x06;
                    stickHat = (byte)((stickHat + rotation) % 8);
                }

                switch (stickHat)
                {
                    case 0x00: offsetY = _stickCal[1]; break; // top
                    case 0x01: offsetX = _stickCal[0]; offsetY = _stickCal[1]; break; // top right
                    case 0x02: offsetX = _stickCal[0]; break; // right
                    case 0x03: offsetX = _stickCal[0]; offsetY = -_stickCal[5]; break; // bottom right
                    case 0x04: offsetY = -_stickCal[5]; break; // bottom
                    case 0x05: offsetX = -_stickCal[4]; offsetY = -_stickCal[5]; break; // bottom left
                    case 0x06: offsetX = -_stickCal[4]; break; // left
                    case 0x07: offsetX = -_stickCal[4]; offsetY = _stickCal[1]; break; // top left
                    case 0x08: default: break; // center
                }

                _stickPrecal[0] = (ushort)(_stickCal[2] + offsetX);
                _stickPrecal[1] = (ushort)(_stickCal[3] + offsetY);
            }
        }
        else
        {
            throw new NotImplementedException($"Cannot extract sticks values for report {reportType:X}");
        }
    }

    private void ExtractButtonsValues(ReadOnlySpan<byte> reportBuf)
    {
        byte reportType = reportBuf[0];

        if (reportType == (byte)InputReportMode.StandardFull)
        {
            var offset = IsLeft ? 2 : 0;

            _buttons[(int)Button.DpadDown] = (reportBuf[3 + offset] & (IsLeft ? 0x01 : 0x04)) != 0;
            _buttons[(int)Button.DpadRight] = (reportBuf[3 + offset] & (IsLeft ? 0x04 : 0x08)) != 0;
            _buttons[(int)Button.DpadUp] = (reportBuf[3 + offset] & 0x02) != 0;
            _buttons[(int)Button.DpadLeft] = (reportBuf[3 + offset] & (IsLeft ? 0x08 : 0x01)) != 0;
            _buttons[(int)Button.Home] = (reportBuf[4] & 0x10) != 0;
            _buttons[(int)Button.Capture] = (reportBuf[4] & 0x20) != 0;
            _buttons[(int)Button.Minus] = (reportBuf[4] & 0x01) != 0;
            _buttons[(int)Button.Plus] = (reportBuf[4] & 0x02) != 0;
            _buttons[(int)Button.Stick] = (reportBuf[4] & (IsLeft ? 0x08 : 0x04)) != 0;
            _buttons[(int)Button.Shoulder1] = (reportBuf[3 + offset] & 0x40) != 0;
            _buttons[(int)Button.Shoulder2] = (reportBuf[3 + offset] & 0x80) != 0;
            _buttons[(int)Button.SR] = (reportBuf[3 + offset] & 0x10) != 0;
            _buttons[(int)Button.SL] = (reportBuf[3 + offset] & 0x20) != 0;

            if (!IsJoycon)
            {
                _buttons[(int)Button.B] = (reportBuf[3] & 0x04) != 0;
                _buttons[(int)Button.A] = (reportBuf[3] & 0x08) != 0;
                _buttons[(int)Button.X] = (reportBuf[3] & 0x02) != 0;
                _buttons[(int)Button.Y] = (reportBuf[3] & 0x01) != 0;

                _buttons[(int)Button.Shoulder21] = (reportBuf[3] & 0x40) != 0;
                _buttons[(int)Button.Shoulder22] = (reportBuf[3] & 0x80) != 0;

                _buttons[(int)Button.Stick2] = (reportBuf[4] & 0x04) != 0;
            }
        }
        else if (reportType == (byte)InputReportMode.SimpleHID)
        {
            _buttons[(int)Button.Home] = (reportBuf[2] & 0x10) != 0;
            _buttons[(int)Button.Capture] = (reportBuf[2] & 0x20) != 0;
            _buttons[(int)Button.Minus] = (reportBuf[2] & 0x01) != 0;
            _buttons[(int)Button.Plus] = (reportBuf[2] & 0x02) != 0;
            _buttons[(int)Button.Stick] = (reportBuf[2] & (IsLeft ? 0x04 : 0x08)) != 0;

            if (!IsJoycon)
            {
                byte stickHat = reportBuf[3];

                _buttons[(int)Button.DpadDown] = stickHat == 0x03 || stickHat == 0x04 || stickHat == 0x05;
                _buttons[(int)Button.DpadRight] = stickHat == 0x01 || stickHat == 0x02 || stickHat == 0x03;
                _buttons[(int)Button.DpadUp] = stickHat == 0x07 || stickHat == 0x00 || stickHat == 0x01;
                _buttons[(int)Button.DpadLeft] = stickHat == 0x05 || stickHat == 0x06 || stickHat == 0x07;

                _buttons[(int)Button.B] = (reportBuf[1] & 0x01) != 0;
                _buttons[(int)Button.A] = (reportBuf[1] & 0x02) != 0;
                _buttons[(int)Button.X] = (reportBuf[1] & 0x08) != 0;
                _buttons[(int)Button.Y] = (reportBuf[1] & 0x04) != 0;

                _buttons[(int)Button.Shoulder1] = (reportBuf[1] & 0x10) != 0;
                _buttons[(int)Button.Shoulder2] = (reportBuf[1] & 0x40) != 0;
                _buttons[(int)Button.Shoulder21] = (reportBuf[1] & 0x20) != 0;
                _buttons[(int)Button.Shoulder22] = (reportBuf[1] & 0x80) != 0;

                _buttons[(int)Button.Stick2] = (reportBuf[2] & 0x08) != 0;
            }
            else
            {
                _buttons[(int)Button.DpadDown] = (reportBuf[1] & (IsLeft ? 0x02 : 0x04)) != 0;
                _buttons[(int)Button.DpadRight] = (reportBuf[1] & (IsLeft ? 0x08 : 0x01)) != 0;
                _buttons[(int)Button.DpadUp] = (reportBuf[1] & (IsLeft ? 0x04 : 0x02)) != 0;
                _buttons[(int)Button.DpadLeft] = (reportBuf[1] & (IsLeft ? 0x01 : 0x08)) != 0;

                _buttons[(int)Button.Shoulder1] = (reportBuf[2] & 0x40) != 0;
                _buttons[(int)Button.Shoulder2] = (reportBuf[2] & 0x80) != 0;

                _buttons[(int)Button.SR] = (reportBuf[1] & 0x20) != 0;
                _buttons[(int)Button.SL] = (reportBuf[1] & 0x10) != 0;
            }
        }
        else
        {
            throw new NotImplementedException($"Cannot extract buttons values for report {reportType:X}");
        }
    }

    private void ProcessButtonsAndSticks(ReadOnlySpan<byte> reportBuf)
    {
        if (SticksSupported())
        {
            ExtractSticksValues(reportBuf);

            var cal = _stickCal;
            var dz = _deadzone;
            var range = _range;
            var antiDeadzone = Config.StickLeftAntiDeadzone;

            if (_SticksCalibrated)
            {
                cal = _activeStick1;
                dz = Config.StickLeftDeadzone;
                range = Config.StickLeftRange;
            }

            CalculateStickCenter(_stickPrecal, cal, dz, range, antiDeadzone, _stick);

            if (IsPro)
            {
                cal = _stick2Cal;
                dz = _deadzone2;
                range = _range2;
                antiDeadzone = Config.StickRightAntiDeadzone;

                if (_SticksCalibrated)
                {
                    cal = _activeStick2;
                    dz = Config.StickRightDeadzone;
                    range = Config.StickRightRange;
                }

                CalculateStickCenter(_stick2Precal, cal, dz, range, antiDeadzone, _stick2);
            }
            // Read other Joycon's sticks
            else
            {
                Array.Clear(_stick2);
            }

            if (_calibrateSticks)
            {
                var sticks = new SticksData(
                    _stickPrecal[0],
                    _stickPrecal[1],
                    _stick2Precal[0],
                    _stick2Precal[1]
                );
                CalibrationStickDatas.Add(sticks);
            }
            else
            {
                //DebugPrint($"X1={_stick[0]:0.00} Y1={_stick[1]:0.00}. X2={_stick2[0]:0.00} Y2={_stick2[1]:0.00}", DebugType.Threading);
            }
        }

        Array.Clear(_buttons);

        ExtractButtonsValues(reportBuf);
    }

    private void CopyInputFromJoinedController()
    {
        if (!IsJoined)
        {
            return;
        }

        var mainController = IsLeft ? this : Other;
        var OtherController = mainController.Other;

        mainController._buttons[(int)Button.B] = OtherController._buttons[(int)Button.DpadDown];
        mainController._buttons[(int)Button.A] = OtherController._buttons[(int)Button.DpadRight];
        mainController._buttons[(int)Button.X] = OtherController._buttons[(int)Button.DpadUp];
        mainController._buttons[(int)Button.Y] = OtherController._buttons[(int)Button.DpadLeft];

        mainController._buttons[(int)Button.Stick2] = OtherController._buttons[(int)Button.Stick];
        mainController._buttons[(int)Button.Shoulder21] = OtherController._buttons[(int)Button.Shoulder1];
        mainController._buttons[(int)Button.Shoulder22] = OtherController._buttons[(int)Button.Shoulder2];

        mainController._buttons[(int)Button.Home] = OtherController._buttons[(int)Button.Home];
        mainController._buttons[(int)Button.Plus] = OtherController._buttons[(int)Button.Plus];

        Array.Copy(OtherController._stick, mainController._stick2, mainController._stick2.Length);
    }

    // Must be done by all controllers when their input is updated (in the case they are joined)
    private void UpdateInputActivityEachController()
    {
        var activity = false;
        var timestamp = Stopwatch.GetTimestamp();

        if (SticksSupported())
        {
            const float StickActivityThreshold = 0.1f;
            if (MathF.Abs(_stick[0]) > StickActivityThreshold ||
                MathF.Abs(_stick[1]) > StickActivityThreshold ||
                MathF.Abs(_stick2[0]) > StickActivityThreshold ||
                MathF.Abs(_stick2[1]) > StickActivityThreshold)
            {
                activity = true;
            }
        }

        for (var i = 0; i < _buttons.Length; ++i)
        {
            _buttonsUp[i] = _buttonsPrev[i] && !_buttons[i];
            _buttonsDown[i] = !_buttonsPrev[i] && _buttons[i];
            if (_buttonsPrev[i] != _buttons[i])
            {
                _buttonsDownTimestamp[i] = _buttons[i] ? timestamp : -1;
            }

            if (_buttonsUp[i] || _buttonsDown[i])
            {
                activity = true;
            }
        }

        Array.Copy(_buttons, _buttonsPrev, _buttons.Length);

        if (activity)
        {
            _timestampActivity = timestamp;
        }
    }

    private void UpdateInputActivity()
    {
        // Need to update both joined controllers in case they are split afterward
        UpdateInputActivityEachController();

        if (IsJoined)
        {
            Other.UpdateInputActivityEachController();

            // Consider the other joined controller active when the main controller is (so it doesn't power off after splitting)
            var mainController = IsLeft ? this : Other;
            if (mainController._timestampActivity > mainController.Other._timestampActivity)
            {
                mainController.Other._timestampActivity = mainController._timestampActivity;
            }
        }
    }

    private static long TimestampToMs(long timestamp)
    {
        long ticksPerMillisecond = Stopwatch.Frequency / 1000;
        return timestamp / ticksPerMillisecond;
    }

    // Get Gyro/Accel data
    private bool ExtractIMUValues(ReadOnlySpan<byte> reportBuf, int n = 0)
    {
        if (!IMUSupported() || reportBuf[0] != (byte)InputReportMode.StandardFull)
        {
            return false;
        }

        var offset = n * 12;

        _gyrRaw[0] = BitWrangler.EncodeBytesAsWordLittleEndianSigned(reportBuf[19 + offset], reportBuf[20 + offset]);
        _gyrRaw[1] = BitWrangler.EncodeBytesAsWordLittleEndianSigned(reportBuf[21 + offset], reportBuf[22 + offset]);
        _gyrRaw[2] = BitWrangler.EncodeBytesAsWordLittleEndianSigned(reportBuf[23 + offset], reportBuf[24 + offset]);
        _accRaw[0] = BitWrangler.EncodeBytesAsWordLittleEndianSigned(reportBuf[13 + offset], reportBuf[14 + offset]);
        _accRaw[1] = BitWrangler.EncodeBytesAsWordLittleEndianSigned(reportBuf[15 + offset], reportBuf[16 + offset]);
        _accRaw[2] = BitWrangler.EncodeBytesAsWordLittleEndianSigned(reportBuf[17 + offset], reportBuf[18 + offset]);

        if (_calibrateIMU)
        {
            // We need to add the accelerometer offset from the origin position when it's on a flat surface
            short[] accOffset;
            if (IsPro)
            {
                accOffset = _accProHorOffset;
            }
            else if (IsLeft)
            {
                accOffset = _accLeftHorOffset;
            }
            else
            {
                accOffset = _accRightHorOffset;
            }

            var imuData = new IMUData(
                _gyrRaw[0],
                _gyrRaw[1],
                _gyrRaw[2],
                (short)(_accRaw[0] - accOffset[0]),
                (short)(_accRaw[1] - accOffset[1]),
                (short)(_accRaw[2] - accOffset[2])
            );
            CalibrationIMUDatas.Add(imuData);
        }

        var direction = IsLeft ? 1 : -1;

        if (_IMUCalibrated)
        {
            _accG.X = (_accRaw[0] - _activeIMUData[3]) * (1.0f / (_accSensiti[0] - _accNeutral[0])) * 4.0f;
            _gyrG.X = (_gyrRaw[0] - _activeIMUData[0]) * (816.0f / (_gyrSensiti[0] - _activeIMUData[0]));

            _accG.Y = direction * (_accRaw[1] - _activeIMUData[4]) * (1.0f / (_accSensiti[1] - _accNeutral[1])) * 4.0f;
            _gyrG.Y = -direction * (_gyrRaw[1] - _activeIMUData[1]) * (816.0f / (_gyrSensiti[1] - _activeIMUData[1]));

            _accG.Z = direction * (_accRaw[2] - _activeIMUData[5]) * (1.0f / (_accSensiti[2] - _accNeutral[2])) * 4.0f;
            _gyrG.Z = -direction * (_gyrRaw[2] - _activeIMUData[2]) * (816.0f / (_gyrSensiti[2] - _activeIMUData[2]));
        }
        else
        {
            _accG.X = _accRaw[0] * (1.0f / (_accSensiti[0] - _accNeutral[0])) * 4.0f;
            _gyrG.X = (_gyrRaw[0] - _gyrNeutral[0]) * (816.0f / (_gyrSensiti[0] - _gyrNeutral[0]));

            _accG.Y = direction * _accRaw[1] * (1.0f / (_accSensiti[1] - _accNeutral[1])) * 4.0f;
            _gyrG.Y = -direction * (_gyrRaw[1] - _gyrNeutral[1]) * (816.0f / (_gyrSensiti[1] - _gyrNeutral[1]));

            _accG.Z = direction * _accRaw[2] * (1.0f / (_accSensiti[2] - _accNeutral[2])) * 4.0f;
            _gyrG.Z = -direction * (_gyrRaw[2] - _gyrNeutral[2]) * (816.0f / (_gyrSensiti[2] - _gyrNeutral[2]));
        }

        if (IsJoycon && Other == null)
        {
            // single joycon mode; Z do not swap, rest do
            if (IsLeft)
            {
                _accG.X = -_accG.X;
                _accG.Y = -_accG.Y;
                _gyrG.X = -_gyrG.X;
            }
            else
            {
                _gyrG.Y = -_gyrG.Y;
            }

            var temp = _accG.X;
            _accG.X = _accG.Y;
            _accG.Y = -temp;

            temp = _gyrG.X;
            _gyrG.X = _gyrG.Y;
            _gyrG.Y = temp;
        }

        // Update rotation Quaternion
        var degToRad = 0.0174533f;
        _AHRS.Update(
            _gyrG.X * degToRad,
            _gyrG.Y * degToRad,
            _gyrG.Z * degToRad,
            _accG.X,
            _accG.Y,
            _accG.Z
        );

        return true;
    }

    public void Begin()
    {
        if (_receiveReportsThread != null || _sendCommandsThread != null)
        {
            Log("Poll thread cannot start!", Logger.LogLevel.Error);
            return;
        }

        _ctsCommunications ??= new();

        _receiveReportsThread = new Thread(
            () =>
            {
                try
                {
                    ReceiveReports(_ctsCommunications.Token);
                    Log("Thread receive reports finished.", Logger.LogLevel.Debug);
                }
                catch (OperationCanceledException) when (_ctsCommunications.IsCancellationRequested)
                {
                    Log("Thread receive reports canceled.", Logger.LogLevel.Debug);
                }
                catch (Exception e)
                {
                    Log("Thread receive reports error.", e);
                    throw;
                }
            }
        )
        {
            IsBackground = true
        };

        _sendCommandsThread = new Thread(
            () =>
            {
                try
                {
                    SendCommands(_ctsCommunications.Token);
                    Log("Thread send commands finished.", Logger.LogLevel.Debug);
                }
                catch (OperationCanceledException) when (_ctsCommunications.IsCancellationRequested)
                {
                    Log("Thread send commands canceled.", Logger.LogLevel.Debug);
                }
                catch (Exception e)
                {
                    Log("Thread send commands error.", e);
                    throw;
                }
            }
        )
        {
            IsBackground = true
        };

        _sendCommandsThread.Start();
        Log("Thread send commands started.", Logger.LogLevel.Debug);

        _receiveReportsThread.Start();
        Log("Thread receive reports started.", Logger.LogLevel.Debug);

        Log("Ready.");
    }

    private void CalculateStickCenter(ushort[] vals, ushort[] cal, float deadzone, float range, float[] antiDeadzone, float[] stick)
    {
        float dx = vals[0] - cal[2];
        float dy = vals[1] - cal[3];

        float normalizedX = dx / (dx > 0 ? cal[0] : cal[4]);
        float normalizedY = dy / (dy > 0 ? cal[1] : cal[5]);

        float magnitude = MathF.Sqrt(normalizedX * normalizedX + normalizedY * normalizedY);

        if (magnitude <= deadzone || range <= deadzone)
        {
            // Inner deadzone
            stick[0] = 0.0f;
            stick[1] = 0.0f;
        }
        else
        {
            float normalizedMagnitudeX = Math.Min(1.0f, (magnitude - deadzone) / (range - deadzone));
            float normalizedMagnitudeY = normalizedMagnitudeX;

            if (antiDeadzone[0] > 0.0f)
            {
                normalizedMagnitudeX = antiDeadzone[0] + (1.0f - antiDeadzone[0]) * normalizedMagnitudeX;
            }

            if (antiDeadzone[1] > 0.0f)
            {
                normalizedMagnitudeY = antiDeadzone[1] + (1.0f - antiDeadzone[1]) * normalizedMagnitudeY;
            }

            normalizedX *= normalizedMagnitudeX / magnitude;
            normalizedY *= normalizedMagnitudeY / magnitude;

            if (!Config.SticksSquared || normalizedX == 0f || normalizedY == 0f)
            {
                stick[0] = normalizedX;
                stick[1] = normalizedY;
            }
            else
            {
                // Expand the circle to a square area
                if (Math.Abs(normalizedX) > Math.Abs(normalizedY))
                {
                    stick[0] = Math.Sign(normalizedX) * normalizedMagnitudeX;
                    stick[1] = stick[0] * normalizedY / normalizedX;
                }
                else
                {
                    stick[1] = Math.Sign(normalizedY) * normalizedMagnitudeY;
                    stick[0] = stick[1] * normalizedX / normalizedY;
                }
            }

            stick[0] = Math.Clamp(stick[0], -1.0f, 1.0f);
            stick[1] = Math.Clamp(stick[1], -1.0f, 1.0f);
        }
    }

    private static short CastStickValue(float stickValue)
    {
        return (short)MathF.Round(stickValue * (stickValue > 0 ? short.MaxValue : -short.MinValue));
    }

    private static byte CastStickValueByte(float stickValue)
    {
        return (byte)MathF.Round((stickValue + 1.0f) * 0.5F * byte.MaxValue);
    }

    public void SetRumble(float lowFreq, float highFreq, float lowAmplitude, float highAmplitude)
    {
        if (State <= Status.Attached)
        {
            return;
        }

        _rumbles.Enqueue(lowFreq, highFreq, lowAmplitude, highAmplitude);
    }

    // Run from poll thread
    private void SendRumble(Span<byte> buf, ReadOnlySpan<byte> data)
    {
        buf.Clear();

        buf[0] = 0x10;
        buf[1] = (byte)(_globalCount & 0x0F);
        ++_globalCount;

        data[..8].CopyTo(buf[2..]);
        PrintArray<byte>(buf[..10], DebugType.Rumble, format: "Rumble data sent: {0:S}");
        Write(buf);
    }

    private int Subcommand(SubCommandOperation sc, ReadOnlySpan<byte> bufParameters, bool print = true)
    {
        if (!_device.IsValid)
        {
            return DeviceErroredCode;
        }

        var subCommandPacket = new SubCommandPacket(sc, _globalCount, bufParameters, _rumbleBuf, IsUSB);
        ++_globalCount;

        if (print)
        {
            DebugPrint(subCommandPacket, DebugType.Comms);
        }

        int length = Write(subCommandPacket);

        return length;
    }

    private int SubcommandCheck(SubCommandOperation sc, ReadOnlySpan<byte> bufParameters, bool print = true)
    {
        Span<byte> response = stackalloc byte[ReportLength];

        return SubcommandCheck(sc, bufParameters, response, print);
    }

    private int SubcommandCheck(SubCommandOperation sc, ReadOnlySpan<byte> bufParameters, Span<byte> response, bool print = true)
    {
        int length = Subcommand(sc, bufParameters, print);
        if (length <= 0)
        {
            DebugPrint($"Subcommand write error: {ErrorMessage()}", DebugType.Comms);
            return length;
        }

        int tries = 0;
        bool responseFound;
        do
        {
            length = Read(response, 100); // don't set the timeout lower than 100 or might not always work
            responseFound = length >= 20 && response[0] == 0x21 && response[14] == (byte)sc;

            if (length < 0)
            {
                DebugPrint($"Subcommand read error: {ErrorMessage()}", DebugType.Comms);
            }

            tries++;
        } while (tries < 10 && !responseFound && length >= 0);

        if (!responseFound)
        {
            DebugPrint("No response.", DebugType.Comms);
            return length <= 0 ? length : 0;
        }

        if (print)
        {
            PrintArray<byte>(
                response[1..length],
                DebugType.Comms,
                $"Response ID {response[0]:X2}. Data: {{0:S}}"
            );
        }

        return length;
    }

    private static float CalculateDeadzone(ushort[] cal, ushort deadzone)
    {
        return 2.0f * deadzone / Math.Max(cal[0] + cal[4], cal[1] + cal[5]);
    }

    private static float CalculateRange(ushort range)
    {
        return (float)range / 0xFFF;
    }

    private bool CalibrationDataSupported()
    {
        return !IsThirdParty && (IsJoycon || IsPro || IsN64);
    }

    private bool SticksSupported()
    {
        return IsJoycon || IsPro || IsN64;
    }

    public bool IMUSupported()
    {
        return IsJoycon || IsPro;
    }

    public bool HomeLightSupported()
    {
        return Type == ControllerType.JoyconRight || IsPro;
    }

    private bool UseGyroAnalogSliders()
    {
        return Config.GyroAnalogSliders && IMUSupported() && (!IsJoycon || Other != null);
    }

    private bool DumpCalibrationData()
    {
        if (!CalibrationDataSupported())
        {
            // Use default joycon values for sensors
            Array.Fill(_accSensiti, (short)16384);
            Array.Fill(_accNeutral, (short)0);
            Array.Fill(_gyrSensiti, (short)13371);
            Array.Fill(_gyrNeutral, (short)0);

            // Default stick calibration
            Array.Fill(_stickCal, (ushort)2048);
            Array.Fill(_stick2Cal, (ushort)2048);

            _deadzone = Config.StickLeftDeadzone;
            _deadzone2 = Config.StickRightDeadzone;

            _range = Config.StickLeftRange;
            _range2 = Config.StickRightRange;

            _DumpedCalibration = false;

            return true;
        }

        var ok = true;

        // get user calibration data if possible

        // Sticks axis
        {
            var userStickData = ReadSPICheck(SPIPage.UserStickCalibration, ref ok);
            var factoryStickData = ReadSPICheck(SPIPage.FactoryStickCalibration, ref ok);

            var stick1Data = new ReadOnlySpan<byte>(userStickData, IsLeft ? 2 : 13, 9);
            var stick1Name = IsLeft ? "left" : "right";

            if (ok)
            {
                if (userStickData[IsLeft ? 0 : 11] == 0xB2 && userStickData[IsLeft ? 1 : 12] == 0xA1)
                {
                    DebugPrint($"Retrieve user {stick1Name} stick calibration data.", DebugType.Comms);
                }
                else
                {
                    stick1Data = new ReadOnlySpan<byte>(factoryStickData, IsLeft ? 0 : 9, 9);

                    DebugPrint($"Retrieve factory {stick1Name} stick calibration data.", DebugType.Comms);
                }
            }

            _stickCal[IsLeft ? 0 : 2] = BitWrangler.Lower3NibblesLittleEndian(stick1Data[0], stick1Data[1]); // X Axis Max above center
            _stickCal[IsLeft ? 1 : 3] = BitWrangler.Upper3NibblesLittleEndian(stick1Data[1], stick1Data[2]); // Y Axis Max above center
            _stickCal[IsLeft ? 2 : 4] = BitWrangler.Lower3NibblesLittleEndian(stick1Data[3], stick1Data[4]); // X Axis Center
            _stickCal[IsLeft ? 3 : 5] = BitWrangler.Upper3NibblesLittleEndian(stick1Data[4], stick1Data[5]); // Y Axis Center
            _stickCal[IsLeft ? 4 : 0] = BitWrangler.Lower3NibblesLittleEndian(stick1Data[6], stick1Data[7]); // X Axis Min below center
            _stickCal[IsLeft ? 5 : 1] = BitWrangler.Upper3NibblesLittleEndian(stick1Data[7], stick1Data[8]); // Y Axis Min below center

            PrintArray<ushort>(_stickCal.AsSpan()[..6], format: $"{stick1Name} stick 1 calibration data: {{0:S}}");

            if (IsPro)
            {
                var stick2Data = new ReadOnlySpan<byte>(userStickData, !IsLeft ? 2 : 13, 9);
                var stick2Name = !IsLeft ? "left" : "right";

                if (ok)
                {
                    if (userStickData[!IsLeft ? 0 : 11] == 0xB2 && userStickData[!IsLeft ? 1 : 12] == 0xA1)
                    {
                        DebugPrint($"Retrieve user {stick2Name} stick calibration data.", DebugType.Comms);
                    }
                    else
                    {
                        stick2Data = new ReadOnlySpan<byte>(factoryStickData, !IsLeft ? 0 : 9, 9);

                        DebugPrint($"Retrieve factory {stick2Name} stick calibration data.", DebugType.Comms);
                    }
                }

                _stick2Cal[!IsLeft ? 0 : 2] = BitWrangler.Lower3NibblesLittleEndian(stick2Data[0], stick2Data[1]); // X Axis Max above center
                _stick2Cal[!IsLeft ? 1 : 3] = BitWrangler.Upper3NibblesLittleEndian(stick2Data[1], stick2Data[2]); // Y Axis Max above center
                _stick2Cal[!IsLeft ? 2 : 4] = BitWrangler.Lower3NibblesLittleEndian(stick2Data[3], stick2Data[4]); // X Axis Center
                _stick2Cal[!IsLeft ? 3 : 5] = BitWrangler.Upper3NibblesLittleEndian(stick2Data[4], stick2Data[5]); // Y Axis Center
                _stick2Cal[!IsLeft ? 4 : 0] = BitWrangler.Lower3NibblesLittleEndian(stick2Data[6], stick2Data[7]); // X Axis Min below center
                _stick2Cal[!IsLeft ? 5 : 1] = BitWrangler.Upper3NibblesLittleEndian(stick2Data[7], stick2Data[8]); // Y Axis Min below center

                PrintArray<ushort>(_stick2Cal.AsSpan()[..6], format: $"{stick2Name} stick calibration data: {{0:S}}");
            }
        }

        // Sticks deadzones and ranges
        // Looks like the range is a 12 bits precision ratio.
        // I suppose the right way to interpret it is as a float by dividing it by 0xFFF
        {
            var factoryDeadzoneData = ReadSPICheck(SPIPage.StickDeadZone, ref ok);

            var offset = IsLeft ? 0 : 0x12;

            var deadzone = BitWrangler.Lower3NibblesLittleEndian(factoryDeadzoneData[0 + offset], factoryDeadzoneData[1 + offset]);
            _deadzone = CalculateDeadzone(_stickCal, deadzone);

            var range = BitWrangler.Upper3NibblesLittleEndian(factoryDeadzoneData[1 + offset], factoryDeadzoneData[2 + offset]);
            _range = CalculateRange(range);

            if (IsPro)
            {
                offset = !IsLeft ? 0 : 0x12;

                var deadzone2 = BitWrangler.Lower3NibblesLittleEndian(factoryDeadzoneData[0 + offset], factoryDeadzoneData[1 + offset]);
                _deadzone2 = CalculateDeadzone(_stick2Cal, deadzone2);

                var range2 = BitWrangler.Upper3NibblesLittleEndian(factoryDeadzoneData[1 + offset], factoryDeadzoneData[2 + offset]);
                _range2 = CalculateRange(range2);
            }
        }

        // Gyro and accelerometer
        if (IMUSupported())
        {
            var userSensorData = ReadSPICheck(SPIPage.UserMotionCalibration, ref ok);
            var sensorData = new ReadOnlySpan<byte>(userSensorData, 2, 24);

            if (ok)
            {
                if (userSensorData[0] == 0xB2 && userSensorData[1] == 0xA1)
                {
                    DebugPrint("Retrieve user sensors calibration data.", DebugType.Comms);
                }
                else
                {
                    var factorySensorData = ReadSPICheck(SPIPage.FactoryMotionCalibration, ref ok);
                    sensorData = new ReadOnlySpan<byte>(factorySensorData, 0, 24);

                    DebugPrint("Retrieve factory sensors calibration data.", DebugType.Comms);
                }
            }

            _accNeutral[0] = BitWrangler.EncodeBytesAsWordLittleEndianSigned(sensorData[0], sensorData[1]);
            _accNeutral[1] = BitWrangler.EncodeBytesAsWordLittleEndianSigned(sensorData[2], sensorData[3]);
            _accNeutral[2] = BitWrangler.EncodeBytesAsWordLittleEndianSigned(sensorData[4], sensorData[5]);

            _accSensiti[0] = BitWrangler.EncodeBytesAsWordLittleEndianSigned(sensorData[6], sensorData[7]);
            _accSensiti[1] = BitWrangler.EncodeBytesAsWordLittleEndianSigned(sensorData[8], sensorData[9]);
            _accSensiti[2] = BitWrangler.EncodeBytesAsWordLittleEndianSigned(sensorData[10], sensorData[11]);

            _gyrNeutral[0] = BitWrangler.EncodeBytesAsWordLittleEndianSigned(sensorData[12], sensorData[13]);
            _gyrNeutral[1] = BitWrangler.EncodeBytesAsWordLittleEndianSigned(sensorData[14], sensorData[15]);
            _gyrNeutral[2] = BitWrangler.EncodeBytesAsWordLittleEndianSigned(sensorData[16], sensorData[17]);

            _gyrSensiti[0] = BitWrangler.EncodeBytesAsWordLittleEndianSigned(sensorData[18], sensorData[19]);
            _gyrSensiti[1] = BitWrangler.EncodeBytesAsWordLittleEndianSigned(sensorData[20], sensorData[21]);
            _gyrSensiti[2] = BitWrangler.EncodeBytesAsWordLittleEndianSigned(sensorData[22], sensorData[23]);

            bool noCalibration = false;

            if (_accNeutral[0] == -1 || _accNeutral[1] == -1 || _accNeutral[2] == -1)
            {
                Array.Fill(_accNeutral, (short)0);
                noCalibration = true;
            }

            if (_accSensiti[0] == -1 || _accSensiti[1] == -1 || _accSensiti[2] == -1)
            {
                // Default accelerometer sensitivity for joycons
                Array.Fill(_accSensiti, (short)16384);
                noCalibration = true;
            }

            if (_gyrNeutral[0] == -1 || _gyrNeutral[1] == -1 || _gyrNeutral[2] == -1)
            {
                Array.Fill(_gyrNeutral, (short)0);
                noCalibration = true;
            }

            if (_gyrSensiti[0] == -1 || _gyrSensiti[1] == -1 || _gyrSensiti[2] == -1)
            {
                // Default gyroscope sensitivity for joycons
                Array.Fill(_gyrSensiti, (short)13371);
                noCalibration = true;
            }

            if (noCalibration)
            {
                Log("Some sensor calibrations datas are missing, fallback to default ones.", Logger.LogLevel.Warning);
            }

            PrintArray<short>(_gyrNeutral.AsSpan()[..3], type: DebugType.IMU, format: "Gyro neutral position: {0:S}");
        }

        if (!ok)
        {
            Log("Error while reading calibration datas.", Logger.LogLevel.Error);
        }

        _DumpedCalibration = ok;

        return ok;
    }

    public void SetCalibration(bool userCalibration)
    {
        if (userCalibration)
        {
            GetActiveIMUData();
            GetActiveSticksData();
        }
        else
        {
            _IMUCalibrated = false;
            _SticksCalibrated = false;
        }

        var calibrationType = _SticksCalibrated ? "user" : _DumpedCalibration ? "controller" : "default";
        Log($"Using {calibrationType} sticks calibration.");

        if (IMUSupported())
        {
            calibrationType = _IMUCalibrated ? "user" : _DumpedCalibration ? "controller" : "default";
            Log($"Using {calibrationType} sensors calibration.");
        }
    }

    private int Read(Span<byte> response, int timeout = 100)
    {
        if (response.Length < ReportLength)
        {
            throw new IndexOutOfRangeException();
        }

        if (IsDeviceError)
        {
            return DeviceErroredCode;
        }

        if (timeout >= 0)
        {
            return _device.ReadTimeout(response, ReportLength, timeout);
        }

        return _device.Read(response, ReportLength);
    }

    private int Write(ReadOnlySpan<byte> command)
    {
        if (command.Length < _CommandLength)
        {
            throw new IndexOutOfRangeException();
        }

        if (IsDeviceError)
        {
            return DeviceErroredCode;
        }

        return _device.Write(command, _CommandLength);
    }

    private string ErrorMessage()
    {
        if (IsDeviceError)
        {
            return $"Device unavailable: {State}";
        }

        if (!_device.IsValid)
        {
            return "Null handle";
        }

        return _device.GetError();
    }

    private bool USBCommand(byte command, bool print = true)
    {
        if (!_device.IsValid)
        {
            return false;
        }

        Span<byte> buf = stackalloc byte[_CommandLength];
        buf.Clear();

        buf[0] = 0x80;
        buf[1] = command;

        if (print)
        {
            DebugPrint($"USB command {command:X2} sent.", DebugType.Comms);
        }

        int length = Write(buf);

        return length > 0;
    }

    private int USBCommandCheck(byte command, bool print = true)
    {
        Span<byte> response = stackalloc byte[ReportLength];

        return USBCommandCheck(command, response, print);
    }

    private int USBCommandCheck(byte command, Span<byte> response, bool print = true)
    {
        if (!USBCommand(command, print))
        {
            DebugPrint($"USB command write error: {ErrorMessage()}", DebugType.Comms);
            return 0;
        }

        int tries = 0;
        int length;
        bool responseFound;

        do
        {
            length = Read(response, 100);
            responseFound = length > 1 && response[0] == 0x81 && response[1] == command;

            if (length < 0)
            {
                DebugPrint($"USB command read error: {ErrorMessage()}", DebugType.Comms);
            }

            ++tries;
        } while (tries < 10 && !responseFound && length >= 0);

        if (!responseFound)
        {
            DebugPrint("No USB response.", DebugType.Comms);
            return 0;
        }

        if (print)
        {
            PrintArray<byte>(
                response[1..length],
                DebugType.Comms,
                $"USB response ID {response[0]:X2}. Data: {{0:S}}"
            );
        }

        return length;
    }

    private byte[] ReadSPICheck(SPIPage page, ref bool ok, bool print = false)
    {
        var readBuf = new byte[page.PageSize];
        if (!ok)
        {
            return readBuf;
        }

        Span<byte> response = stackalloc byte[ReportLength];

        ok = false;
        for (var i = 0; i < 5; ++i)
        {
            int length = SubcommandCheck(SubCommandOperation.SPIFlashRead, page, response, false);
            if (length >= 20 + page.PageSize && response[15] == page.LowAddress && response[16] == page.HighAddress)
            {
                ok = true;
                break;
            }
        }

        if (ok)
        {
            response.Slice(20, page.PageSize).CopyTo(readBuf);
            if (print)
            {
                PrintArray<byte>(readBuf.AsSpan()[..page.PageSize], DebugType.Comms);
            }
        }
        else
        {
            Log("ReadSPI error.", Logger.LogLevel.Error);
        }

        return readBuf;
    }

    private void PrintArray<T>(
        ReadOnlySpan<T> array,
        DebugType type = DebugType.None,
        string format = "{0:S}"
    )
    {
        if (!ShouldLog(type))
        {
            return;
        }

        var arrayAsStr = string.Empty;

        if (!array.IsEmpty)
        {
            var output = new StringBuilder();

            var elementFormat = array[0] switch
            {
                byte => "{0:X2} ",
                float => "{0:F} ",
                _ => "{0:D} "
            };

            foreach (var element in array)
            {
                output.AppendFormat(elementFormat, element);
            }

            arrayAsStr = output.ToString(0, output.Length - 1); // Remove trailing space
        }

        Log(string.Format(format, arrayAsStr), Logger.LogLevel.Debug, type);
    }

    public class StateChangedEventArgs : EventArgs
    {
        public Status State { get; }

        public StateChangedEventArgs(Status state)
        {
            State = state;
        }
    }

    public static DpadDirection GetDirection(bool up, bool down, bool left, bool right)
    {
        // Avoid conflicting outputs
        if (up && down)
        {
            up = false;
            down = false;
        }

        if (left && right)
        {
            left = false;
            right = false;
        }

        if (up)
        {
            if (left) return DpadDirection.Northwest;
            if (right) return DpadDirection.Northeast;
            return DpadDirection.North;
        }

        if (down)
        {
            if (left) return DpadDirection.Southwest;
            if (right) return DpadDirection.Southeast;
            return DpadDirection.South;
        }

        if (left)
        {
            return DpadDirection.West;
        }

        if (right)
        {
            return DpadDirection.East;
        }

        return DpadDirection.None;
    }

    private static OutputControllerXbox360InputState MapToXbox360Input(Joycon input)
    {
        var output = new OutputControllerXbox360InputState();

        var isN64 = input.IsN64;
        var isJoycon = input.IsJoycon;
        var isLeft = input.IsLeft;
        var other = input.Other;

        var buttons = input._buttonsRemapped;
        var stick = input._stick;
        var stick2 = input._stick2;
        var sliderVal = input._sliderVal;

        var gyroAnalogSliders = input.UseGyroAnalogSliders();
        var swapAB = input.Config.SwapAB;
        var swapXY = input.Config.SwapXY;

        if (other != null && !isLeft)
        {
            gyroAnalogSliders = other.UseGyroAnalogSliders();
            swapAB = other.Config.SwapAB;
            swapXY = other.Config.SwapXY;
        }

        if (isJoycon)
        {
            if (other != null) // no need for && other != this
            {
                output.A = buttons[(int)(isLeft ? Button.B : Button.DpadDown)];
                output.B = buttons[(int)(isLeft ? Button.A : Button.DpadRight)];
                output.X = buttons[(int)(isLeft ? Button.Y : Button.DpadLeft)];
                output.Y = buttons[(int)(isLeft ? Button.X : Button.DpadUp)];

                output.DpadUp = buttons[(int)(isLeft ? Button.DpadUp : Button.X)];
                output.DpadDown = buttons[(int)(isLeft ? Button.DpadDown : Button.B)];
                output.DpadLeft = buttons[(int)(isLeft ? Button.DpadLeft : Button.Y)];
                output.DpadRight = buttons[(int)(isLeft ? Button.DpadRight : Button.A)];

                output.Back = buttons[(int)Button.Minus];
                output.Start = buttons[(int)Button.Plus];
                output.Guide = buttons[(int)Button.Home];

                output.ShoulderLeft = buttons[(int)(isLeft ? Button.Shoulder1 : Button.Shoulder21)];
                output.ShoulderRight = buttons[(int)(isLeft ? Button.Shoulder21 : Button.Shoulder1)];

                output.ThumbStickLeft = buttons[(int)(isLeft ? Button.Stick : Button.Stick2)];
                output.ThumbStickRight = buttons[(int)(isLeft ? Button.Stick2 : Button.Stick)];
            }
            else
            {
                // single joycon in horizontal
                output.A = buttons[(int)(isLeft ? Button.DpadLeft : Button.DpadRight)];
                output.B = buttons[(int)(isLeft ? Button.DpadDown : Button.DpadUp)];
                output.X = buttons[(int)(isLeft ? Button.DpadUp : Button.DpadDown)];
                output.Y = buttons[(int)(isLeft ? Button.DpadRight : Button.DpadLeft)];

                output.Back = buttons[(int)Button.Minus] | buttons[(int)Button.Home];
                output.Start = buttons[(int)Button.Plus] | buttons[(int)Button.Capture];

                output.ShoulderLeft = buttons[(int)Button.SL];
                output.ShoulderRight = buttons[(int)Button.SR];

                output.ThumbStickLeft = buttons[(int)Button.Stick];
            }
        }
        else if (isN64)
        {
            // Mapping at https://github.com/dekuNukem/Nintendo_Switch_Reverse_Engineering/pull/133/files

            output.A = buttons[(int)Button.B];
            output.B = buttons[(int)Button.A];

            output.DpadUp = buttons[(int)Button.DpadUp];
            output.DpadDown = buttons[(int)Button.DpadDown];
            output.DpadLeft = buttons[(int)Button.DpadLeft];
            output.DpadRight = buttons[(int)Button.DpadRight];

            output.Start = buttons[(int)Button.Plus];
            output.Guide = buttons[(int)Button.Home];

            output.ShoulderLeft = buttons[(int)Button.Shoulder1];
            output.ShoulderRight = buttons[(int)Button.Shoulder21];
        }
        else
        {
            output.A = buttons[(int)Button.B];
            output.B = buttons[(int)Button.A];
            output.Y = buttons[(int)Button.X];
            output.X = buttons[(int)Button.Y];

            output.DpadUp = buttons[(int)Button.DpadUp];
            output.DpadDown = buttons[(int)Button.DpadDown];
            output.DpadLeft = buttons[(int)Button.DpadLeft];
            output.DpadRight = buttons[(int)Button.DpadRight];

            output.Back = buttons[(int)Button.Minus];
            output.Start = buttons[(int)Button.Plus];
            output.Guide = buttons[(int)Button.Home];

            output.ShoulderLeft = buttons[(int)Button.Shoulder1];
            output.ShoulderRight = buttons[(int)Button.Shoulder21];

            output.ThumbStickLeft = buttons[(int)Button.Stick];
            output.ThumbStickRight = buttons[(int)Button.Stick2];
        }

        if (input.SticksSupported())
        {
            if (isJoycon && other == null)
            {
                output.AxisLeftY = CastStickValue((isLeft ? 1 : -1) * stick[0]);
                output.AxisLeftX = CastStickValue((isLeft ? -1 : 1) * stick[1]);
            }
            else if (isN64)
            {
                output.AxisLeftX = CastStickValue(stick[0]);
                output.AxisLeftY = CastStickValue(stick[1]);

                // C buttons mapped to right stick
                output.AxisRightX = CastStickValue((buttons[(int)Button.X] ? -1 : 0) + (buttons[(int)Button.Minus] ? 1 : 0));
                output.AxisRightY = CastStickValue((buttons[(int)Button.Shoulder22] ? -1 : 0) + (buttons[(int)Button.Y] ? 1 : 0));
            }
            else
            {
                output.AxisLeftX = CastStickValue(other == input && !isLeft ? stick2[0] : stick[0]);
                output.AxisLeftY = CastStickValue(other == input && !isLeft ? stick2[1] : stick[1]);

                output.AxisRightX = CastStickValue(other == input && !isLeft ? stick[0] : stick2[0]);
                output.AxisRightY = CastStickValue(other == input && !isLeft ? stick[1] : stick2[1]);
            }
        }

        if (isJoycon && other == null)
        {
            output.TriggerLeft = (byte)(buttons[(int)(isLeft ? Button.Shoulder2 : Button.Shoulder1)] ? byte.MaxValue : 0);
            output.TriggerRight = (byte)(buttons[(int)(isLeft ? Button.Shoulder1 : Button.Shoulder2)] ? byte.MaxValue : 0);
        }
        else if (isN64)
        {
            output.TriggerLeft = (byte)(buttons[(int)Button.Shoulder2] ? byte.MaxValue : 0);
            output.TriggerRight = (byte)(buttons[(int)Button.Stick] ? byte.MaxValue : 0);
        }
        else
        {
            var lval = gyroAnalogSliders ? sliderVal[0] : byte.MaxValue;
            var rval = gyroAnalogSliders ? sliderVal[1] : byte.MaxValue;
            output.TriggerLeft = (byte)(buttons[(int)(isLeft ? Button.Shoulder2 : Button.Shoulder22)] ? lval : 0);
            output.TriggerRight = (byte)(buttons[(int)(isLeft ? Button.Shoulder22 : Button.Shoulder2)] ? rval : 0);
        }

        // Avoid conflicting output
        if (output.DpadUp && output.DpadDown)
        {
            output.DpadUp = false;
            output.DpadDown = false;
        }

        if (output.DpadLeft && output.DpadRight)
        {
            output.DpadLeft = false;
            output.DpadRight = false;
        }

        if (swapAB)
        {
            (output.A, output.B) = (output.B, output.A);
        }

        if (swapXY)
        {
            (output.X, output.Y) = (output.Y, output.X);
        }

        return output;
    }

    public static OutputControllerDualShock4InputState MapToDualShock4Input(Joycon input)
    {
        var output = new OutputControllerDualShock4InputState();

        var isN64 = input.IsN64;
        var isJoycon = input.IsJoycon;
        var isLeft = input.IsLeft;
        var other = input.Other;

        var buttons = input._buttonsRemapped;
        var stick = input._stick;
        var stick2 = input._stick2;
        var sliderVal = input._sliderVal;

        var gyroAnalogSliders = input.UseGyroAnalogSliders();
        var swapAB = input.Config.SwapAB;
        var swapXY = input.Config.SwapXY;

        if (other != null && !isLeft)
        {
            gyroAnalogSliders = other.UseGyroAnalogSliders();
            swapAB = other.Config.SwapAB;
            swapXY = other.Config.SwapXY;
        }

        if (isJoycon)
        {
            if (other != null) // no need for && other != this
            {
                output.Cross = buttons[(int)(isLeft ? Button.B : Button.DpadDown)];
                output.Circle = buttons[(int)(isLeft ? Button.A : Button.DpadRight)];
                output.Square = buttons[(int)(isLeft ? Button.Y : Button.DpadLeft)];
                output.Triangle = buttons[(int)(isLeft ? Button.X : Button.DpadUp)];

                output.DPad = GetDirection(
                    buttons[(int)(isLeft ? Button.DpadUp : Button.X)],
                    buttons[(int)(isLeft ? Button.DpadDown : Button.B)],
                    buttons[(int)(isLeft ? Button.DpadLeft : Button.Y)],
                    buttons[(int)(isLeft ? Button.DpadRight : Button.A)]
                );

                output.Share = buttons[(int)Button.Capture];
                output.Options = buttons[(int)Button.Plus];
                output.Ps = buttons[(int)Button.Home];
                output.Touchpad = buttons[(int)Button.Minus];

                output.ShoulderLeft = buttons[(int)(isLeft ? Button.Shoulder1 : Button.Shoulder21)];
                output.ShoulderRight = buttons[(int)(isLeft ? Button.Shoulder21 : Button.Shoulder1)];

                output.ThumbLeft = buttons[(int)(isLeft ? Button.Stick : Button.Stick2)];
                output.ThumbRight = buttons[(int)(isLeft ? Button.Stick2 : Button.Stick)];
            }
            else
            {
                // single joycon in horizontal
                output.Cross = buttons[(int)(isLeft ? Button.DpadLeft : Button.DpadRight)];
                output.Circle = buttons[(int)(isLeft ? Button.DpadDown : Button.DpadUp)];
                output.Square = buttons[(int)(isLeft ? Button.DpadUp : Button.DpadDown)];
                output.Triangle = buttons[(int)(isLeft ? Button.DpadRight : Button.DpadLeft)];

                output.Ps = buttons[(int)Button.Minus] | buttons[(int)Button.Home];
                output.Options = buttons[(int)Button.Plus] | buttons[(int)Button.Capture];

                output.ShoulderLeft = buttons[(int)Button.SL];
                output.ShoulderRight = buttons[(int)Button.SR];

                output.ThumbLeft = buttons[(int)Button.Stick];
            }
        }
        else if (isN64)
        {
            output.Cross = buttons[(int)Button.B];
            output.Circle = buttons[(int)Button.A];

            output.DPad = GetDirection(
                buttons[(int)Button.DpadUp],
                buttons[(int)Button.DpadDown],
                buttons[(int)Button.DpadLeft],
                buttons[(int)Button.DpadRight]
            );

            output.Share = buttons[(int)Button.Capture];
            output.Options = buttons[(int)Button.Plus];
            output.Ps = buttons[(int)Button.Home];

            output.ShoulderLeft = buttons[(int)Button.Shoulder1];
            output.ShoulderRight = buttons[(int)Button.Shoulder21];
        }
        else
        {
            output.Cross = buttons[(int)Button.B];
            output.Circle = buttons[(int)Button.A];
            output.Square = buttons[(int)Button.Y];
            output.Triangle = buttons[(int)Button.X];

            output.DPad = GetDirection(
                buttons[(int)Button.DpadUp],
                buttons[(int)Button.DpadDown],
                buttons[(int)Button.DpadLeft],
                buttons[(int)Button.DpadRight]
            );

            output.Share = buttons[(int)Button.Capture];
            output.Options = buttons[(int)Button.Plus];
            output.Ps = buttons[(int)Button.Home];
            output.Touchpad = buttons[(int)Button.Minus];

            output.ShoulderLeft = buttons[(int)Button.Shoulder1];
            output.ShoulderRight = buttons[(int)Button.Shoulder21];

            output.ThumbLeft = buttons[(int)Button.Stick];
            output.ThumbRight = buttons[(int)Button.Stick2];
        }

        if (input.SticksSupported())
        {
            if (isJoycon && other == null)
            {
                output.ThumbLeftY = CastStickValueByte((isLeft ? 1 : -1) * -stick[0]);
                output.ThumbLeftX = CastStickValueByte((isLeft ? 1 : -1) * -stick[1]);

                output.ThumbRightX = CastStickValueByte(0);
                output.ThumbRightY = CastStickValueByte(0);
            }
            else if (isN64)
            {
                output.ThumbLeftX = CastStickValueByte(stick[0]);
                output.ThumbLeftY = CastStickValueByte(-stick[1]);

                // C buttons mapped to right stick
                output.ThumbRightX = CastStickValueByte((buttons[(int)Button.X] ? -1 : 0) + (buttons[(int)Button.Minus] ? 1 : 0));
                output.ThumbRightY = CastStickValueByte((buttons[(int)Button.Shoulder22] ? 1 : 0) + (buttons[(int)Button.Y] ? -1 : 0));
            }
            else
            {
                output.ThumbLeftX = CastStickValueByte(other == input && !isLeft ? stick2[0] : stick[0]);
                output.ThumbLeftY = CastStickValueByte(other == input && !isLeft ? -stick2[1] : -stick[1]);

                output.ThumbRightX = CastStickValueByte(other == input && !isLeft ? stick[0] : stick2[0]);
                output.ThumbRightY = CastStickValueByte(other == input && !isLeft ? -stick[1] : -stick2[1]);

                //input.DebugPrint($"X:{-stick[0]:0.00} Y:{stick[1]:0.00}", DebugType.Threading);
                //input.DebugPrint($"X:{output.ThumbLeftX} Y:{output.ThumbLeftY}", DebugType.Threading);
            }
        }

        if (isJoycon && other == null)
        {
            output.TriggerLeftValue = (byte)(buttons[(int)(isLeft ? Button.Shoulder2 : Button.Shoulder1)] ? byte.MaxValue : 0);
            output.TriggerRightValue = (byte)(buttons[(int)(isLeft ? Button.Shoulder1 : Button.Shoulder2)] ? byte.MaxValue : 0);
        }
        else if (isN64)
        {
            output.TriggerLeftValue = (byte)(buttons[(int)Button.Shoulder2] ? byte.MaxValue : 0);
            output.TriggerRightValue = (byte)(buttons[(int)Button.Stick] ? byte.MaxValue : 0);
        }
        else
        {
            var lval = gyroAnalogSliders ? sliderVal[0] : byte.MaxValue;
            var rval = gyroAnalogSliders ? sliderVal[1] : byte.MaxValue;
            output.TriggerLeftValue = (byte)(buttons[(int)(isLeft ? Button.Shoulder2 : Button.Shoulder22)] ? lval : 0);
            output.TriggerRightValue = (byte)(buttons[(int)(isLeft ? Button.Shoulder22 : Button.Shoulder2)] ? rval : 0);
        }

        // Output digital L2 / R2 in addition to analog L2 / R2
        output.TriggerLeft = output.TriggerLeftValue > 0;
        output.TriggerRight = output.TriggerRightValue > 0;

        if (swapAB)
        {
            (output.Cross, output.Circle) = (output.Circle, output.Cross);
        }

        if (swapXY)
        {
            (output.Square, output.Triangle) = (output.Triangle, output.Square);
        }

        return output;
    }

    public static string GetControllerName(ControllerType type)
    {
        return type switch
        {
            ControllerType.JoyconLeft  => "Left joycon",
            ControllerType.JoyconRight => "Right joycon",
            ControllerType.Pro         => "Pro controller",
            ControllerType.SNES        => "SNES controller",
            ControllerType.NES         => "NES controller",
            ControllerType.FamicomI    => "Famicom I controller",
            ControllerType.FamicomII   => "Famicom II controller",
            ControllerType.N64         => "N64 controller",
            _                          => "Controller"
        };
    }

    public string GetControllerName()
    {
        return GetControllerName(Type);
    }

    public void StartSticksCalibration()
    {
        CalibrationStickDatas.Clear();
        _calibrateSticks = true;
    }

    public void StopSticksCalibration(bool clean = false)
    {
        _calibrateSticks = false;

        if (clean)
        {
            CalibrationStickDatas.Clear();
        }
    }

    public void StartIMUCalibration()
    {
        CalibrationIMUDatas.Clear();
        _calibrateIMU = true;
    }

    public void StopIMUCalibration(bool clean = false)
    {
        _calibrateIMU = false;

        if (clean)
        {
            CalibrationIMUDatas.Clear();
        }
    }

    private void Log(string message, Logger.LogLevel level = Logger.LogLevel.Info, DebugType type = DebugType.None)
    {
        if (level == Logger.LogLevel.Debug && type != DebugType.None)
        {
            _logger?.Log($"[P{PadId + 1}] [{type.ToString().ToUpper()}] {message}", level);
        }
        else
        {
            _logger?.Log($"[P{PadId + 1}] {message}", level);
        }
    }

    private void Log(string message, Exception e, Logger.LogLevel level = Logger.LogLevel.Error)
    {
        _logger?.Log($"[P{PadId + 1}] {message}", e, level);
    }

    public void ApplyConfig(bool showErrors = true)
    {
        var oldConfig = Config.Clone();
        Config.ShowErrors = showErrors;
        Config.Update();

        if (oldConfig.ShowAsXInput != Config.ShowAsXInput)
        {
            if (Config.ShowAsXInput)
            {
                OutXbox.Connect();
            }
            else
            {
                OutXbox.Disconnect();
            }
        }

        if (oldConfig.ShowAsDs4 != Config.ShowAsDs4)
        {
            if (Config.ShowAsDs4)
            {
                OutDs4.Connect();
            }
            else
            {
                OutDs4.Disconnect();
            }
        }

        if (!CalibrationDataSupported())
        {
            if (oldConfig.StickLeftDeadzone != Config.StickLeftDeadzone)
            {
                _deadzone = Config.StickLeftDeadzone;
            }

            if (oldConfig.StickRightDeadzone != Config.StickRightDeadzone)
            {
                _deadzone2 = Config.StickRightDeadzone;
            }

            if (oldConfig.StickLeftRange != Config.StickLeftRange)
            {
                _range = Config.StickLeftRange;
            }

            if (oldConfig.StickRightRange != Config.StickRightRange)
            {
                _range2 = Config.StickRightRange;
            }
        }

        if (oldConfig.AllowCalibration != Config.AllowCalibration)
        {
            SetCalibration(Config.AllowCalibration);
        }
    }

    private class RumbleQueue
    {
        private struct Rumble(float lowFreq, float highFreq, float lowAmplitude, float highAmplitude)
        {
            public float LowFreq = lowFreq;
            public float HighFreq = highFreq;
            public float LowAmplitude = lowAmplitude;
            public float HighAmplitude = highAmplitude;
        }

        private const int MaxRumble = 15;
        private readonly ConcurrentSpinQueue<Rumble> _queue;

        public RumbleQueue()
        {
            _queue = new(MaxRumble);

            var noRumble = new Rumble(0f, 0f, 0f, 0f);
            _queue.Enqueue(noRumble);
        }

        public void Enqueue(float lowFreq, float highFreq, float lowAmplitude, float highAmplitude)
        {
            var rumble = new Rumble(lowFreq, highFreq, lowAmplitude, highAmplitude);
            _queue.Enqueue(rumble);
        }

        private static byte EncodeAmplitude(float amp)
        {
            // Determined with the tables at https://github.com/dekuNukem/Nintendo_Switch_Reverse_Engineering/blob/master/rumble_data_table.md
            return amp switch
            {
                < 0.01182818f => (byte)MathF.Round(10467 * amp * amp + 45.408f * amp),
                < 0.1124807f => (byte)MathF.Round(4 * MathF.Log2(amp) + 27.608f),
                < 0.2249712f => (byte)MathF.Round(16 * MathF.Log2(amp) + 65.435f),
                < 1.003f => (byte)MathF.Round(32 * MathF.Log2(amp) + 99.87f),
                _ => 0
            };
        }

        private static ushort EncodeLowAmplitude(byte encodedAmplitude)
        {
            return (ushort)(ushort.RotateRight(encodedAmplitude, 1) + 0x40);
        }

        private static byte EncodeHighAmplitude(byte encodedAmplitude)
        {
            return (byte)(encodedAmplitude * 2);
        }

        private static byte EncodeFrequency(float frequency)
        {
            return (byte)MathF.Round(32f * MathF.Log2(frequency * 0.1f));
        }

        private static ushort EncodeHighFrequency(float frequency)
        {
            return (ushort)((EncodeFrequency(frequency) - 0x60) * 4);
        }

        private static byte EncodeLowFrequency(float frequency)
        {
            return (byte)(EncodeFrequency(frequency) - 0x40);
        }

        private static void EncodeRumble(Span<byte> rumbleData, float lowFreq, float highFreq, float amplitude)
        {
            if (amplitude <= 0.0f)
            {
                rumbleData[0] = 0x0;
                rumbleData[1] = 0x1;
                rumbleData[2] = 0x40;
                rumbleData[3] = 0x40;

                return;
            }

            var hf = EncodeHighFrequency(highFreq);
            var lf = EncodeLowFrequency(lowFreq);

            var encodedAmplitude = EncodeAmplitude(amplitude);
            var ha = EncodeHighAmplitude(encodedAmplitude);
            var la = EncodeLowAmplitude(encodedAmplitude);

            rumbleData[0] = (byte)(hf & 0xFF);
            rumbleData[1] = (byte)(((hf >> 8) & 0xFF) + ha);
            rumbleData[2] = (byte)(((la >> 8) & 0xFF) + lf);
            rumbleData[3] = (byte)(la & 0xFF);
        }

        public bool TryDequeue(Span<byte> rumbleData)
        {
            if (!_queue.TryDequeue(out var rumble))
            {
                return false;
            }

            rumble.LowFreq = Math.Clamp(rumble.LowFreq, 40.875885f, 626.286133f);
            rumble.HighFreq = Math.Clamp(rumble.HighFreq, 81.75177f, 1252.572266f);
            rumble.LowAmplitude = Math.Clamp(rumble.LowAmplitude, 0.0f, 1.0f);
            rumble.HighAmplitude = Math.Clamp(rumble.HighAmplitude, 0.0f, 1.0f);

            // Left rumble
            EncodeRumble(rumbleData[..4], rumble.LowFreq, rumble.HighFreq, rumble.HighAmplitude);

            // Right rumble
            EncodeRumble(rumbleData.Slice(4, 4), rumble.LowFreq, rumble.HighFreq, rumble.LowAmplitude);

            return true;
        }

        public void Clear()
        {
            _queue.Clear();
        }
    }

    public struct IMUData(short xg, short yg, short zg, short xa, short ya, short za)
    {
        public short Xg = xg;
        public short Yg = yg;
        public short Zg = zg;
        public short Xa = xa;
        public short Ya = ya;
        public short Za = za;
    }

    public struct SticksData(ushort x1, ushort y1, ushort x2, ushort y2)
    {
        public ushort Xs1 = x1;
        public ushort Ys1 = y1;
        public ushort Xs2 = x2;
        public ushort Ys2 = y2;
    }

    private class RollingAverage
    {
        private readonly Queue<int> _samples;
        private readonly int _size;
        private long _sum;

        public RollingAverage(int size)
        {
            _size = size;
            _samples = new Queue<int>(size);
            _sum = 0;
        }

        public void AddValue(int value)
        {
            if (_samples.Count >= _size)
            {
                int sample = _samples.Dequeue();
                _sum -= sample;
            }

            _samples.Enqueue(value);
            _sum += value;
        }

        public void Clear()
        {
            _samples.Clear();
            _sum = 0;
        }

        public bool Empty()
        {
            return _samples.Count == 0;
        }

        public float GetAverage()
        {
            return Empty() ? 0 : _sum / _samples.Count;
        }
    }
}
