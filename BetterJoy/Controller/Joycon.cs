using BetterJoy.Collections;
using BetterJoy.Config;
using BetterJoy.Controller.Mapping;
using BetterJoy.Exceptions;
using BetterJoy.Forms;
using BetterJoy.Hardware.Calibration;
using BetterJoy.Hardware.Data;
using BetterJoy.Hardware.SubCommand;
using BetterJoy.Network;
using BetterJoy.Network.Server;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using WindowsInput.Events;

namespace BetterJoy.Controller;

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
        Motion,
        Rumble,
        Shake,
        Dev
    }

    public enum Status : uint
    {
        NotAttached,
        AttachError,
        Errored,
        Dropped,
        Attached,
        MotionDataOk
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

    private MotionCalibration _motionCalibration = new();

    private readonly MadgwickAHRS _AHRS; // for getting filtered Euler angles of rotation; 5ms sampling rate

    private readonly bool[] _buttons = new bool[20];
    private readonly bool[] _buttonsDown = new bool[20];
    private readonly long[] _buttonsDownTimestamp = new long[20];
    private readonly bool[] _buttonsUp = new bool[20];
    private readonly bool[] _buttonsPrev = new bool[20];
    private readonly bool[] _buttonsRemapped = new bool[20];

    private readonly float[] _curRotation = [0, 0, 0, 0, 0, 0]; // Filtered motion data

    private static readonly byte[] _stopRumbleBuf = [0x0, 0x1, 0x40, 0x40, 0x0, 0x1, 0x40, 0x40]; // Stop rumble
    private readonly byte[] _rumbleBuf;

    private readonly Dictionary<int, bool> _mouseToggleBtn = [];

    // Values from https://github.com/dekuNukem/Nintendo_Switch_Reverse_Engineering/blob/master/spi_flash_notes.md#6-axis-horizontal-offsets
    private readonly ThreeAxisShort _accProHorOffset = new(-688, 0, 4038);
    private readonly ThreeAxisShort _accLeftHorOffset = new(350, 0, 4081);
    private readonly ThreeAxisShort _accRightHorOffset = new(350, 0, -4081);

    private readonly Stopwatch _shakeTimer = Stopwatch.StartNew(); //Setup a timer for measuring shake in milliseconds

    private readonly byte[] _sliderVal = [0, 0];

    private StickLimitsCalibration _stickCal = new();
    private TwoAxisUShort _stickPrecal;

    private StickLimitsCalibration _stick2Cal = new();
    private TwoAxisUShort _stick2Precal;

    private Motion _motion;
    public bool ActiveGyro;

    private bool _DumpedCalibration = false;
    private bool _motionCalibrated = false;
    private bool _SticksCalibrated = false;
    private readonly short[] _activeMotionData = new short[6];
    private StickLimitsCalibration _activeStick1 = new();
    private StickLimitsCalibration _activeStick2 = new();

    public BatteryLevel Battery = BatteryLevel.Unknown;
    public bool Charging = false;

    private StickDeadZoneCalibration _deadZone;
    private StickDeadZoneCalibration _deadZone2;
    private StickRangeCalibration _range;
    private StickRangeCalibration _range2;

    private readonly MainForm _form;
    private readonly Logger? _logger;

    private byte _globalCount;

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

    public MacAddress MacAddress = new();
    public readonly string Path;

    private Thread? _receiveReportsThread;
    private Thread? _sendCommandsThread;

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

    private Stick _stick;
    private Stick _stick2;

    private CancellationTokenSource? _ctsCommunications;
    public ulong Timestamp { get; private set; }
    public readonly long TimestampCreation;

    private long _timestampActivity = Stopwatch.GetTimestamp();

    public ControllerType Type { get; private set; }

    public EventHandler<StateChangedEventArgs>? StateChanged;

    public readonly ConcurrentList<MotionShort> CalibrationMotionDatas = [];
    public readonly ConcurrentList<SticksData> CalibrationStickDatas = [];
    private bool _calibrateSticks = false;
    private bool _calibrateMotion = false;

    private readonly Stopwatch _timeSinceReceive = new();
    private readonly RollingAverage _avgReceiveDeltaMs = new(100); // delta is around 10-16ms, so rolling average over 1000-1600ms

    private volatile bool _pauseSendCommands;
    private volatile bool _sendCommandsPaused;
    private volatile bool _requestPowerOff;
    private volatile bool _requestSetLEDByPadID;

    public Joycon(
        Logger? logger,
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

    [MemberNotNullWhen(true, nameof(Other))]
    public bool IsJoined => Other is not null && !ReferenceEquals(Other, this);

    [MemberNotNullWhen(false, nameof(Other))]
    public bool IsPrimaryGyro => !IsJoined || Config.GyroLeftHanded == IsLeft;


    public bool IsDeviceReady => State > Status.Dropped;
    public bool IsDeviceError => !IsDeviceReady && State != Status.NotAttached;


    public Joycon? Other;

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

    public void GetActiveMotionData()
    {
        var activeMotionData = _form.ActiveCalibrationMotionData(SerialOrMac);

        if (activeMotionData != null)
        {
            Array.Copy(activeMotionData, _activeMotionData, 6);
            _motionCalibrated = true;
        }
        else
        {
            _motionCalibrated = false;
        }
    }

    public void GetActiveSticksData()
    {
        var activeSticksData = _form.ActiveCaliSticksData(SerialOrMac);
        if (activeSticksData != null)
        {
            _activeStick1 = new StickLimitsCalibration(activeSticksData.AsSpan(0, 6));
            _activeStick2 = new StickLimitsCalibration(activeSticksData.AsSpan(6, 6));
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
        if (type == DebugType.None)
        {
            return true;
        }

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

        Log(stringifyable?.ToString() ?? $"Attempted to log type ({typeof(T)}) but was null.", Logger.LogLevel.Debug, type);
    }

    public Motion GetMotion()
    {
        return _motion;
    }

    public bool Reset()
    {
        Log("Resetting connection.");
        return SetHCIState(0x01);
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

            SetMotion(true);
            SetMotionSensitivity();

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
        Span<byte> macBytes = MacAddress;

        if (IsUSB)
        {
            Span<byte> buf = stackalloc byte[ReportLength];

            // Get MAC
            if (USBCommandCheck(0x01, buf) < 10)
            {
                // can occur when USB connection isn't closed properly
                throw new DeviceComFailedException("reset mac");
            }

            buf.Slice(4, macBytes.Length).CopyTo(macBytes);
            macBytes.Reverse();
            SerialOrMac = MacAddress.ToString();
            return;
        }

        // Serial = MAC address of the controller in bluetooth
        try
        {
            for (var n = 0; n < macBytes.Length && n < SerialNumber.Length; n++)
            {
                macBytes[n] = byte.Parse(SerialNumber.AsSpan(n * 2, 2), NumberStyles.HexNumber);
            }
        }
        // could not parse mac address, ignore
        catch (Exception e)
        {
            Log("Cannot parse MAC address.", e, Logger.LogLevel.Debug);
        }
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
        // Bluetooth manual pairing
        byte[] btmac_host = Program.BtMac.GetAddressBytes();

        // send host MAC and acquire Joycon MAC
        SubcommandWithResponse(SubCommandOperation.ManualBluetoothPairing, [0x01, btmac_host[5], btmac_host[4], btmac_host[3], btmac_host[2], btmac_host[1], btmac_host[0]]);
        SubcommandWithResponse(SubCommandOperation.ManualBluetoothPairing, [0x02]); // LTKhash
        SubcommandWithResponse(SubCommandOperation.ManualBluetoothPairing, [0x03]); // save pairing info
    }

    public bool SetPlayerLED(byte leds = 0x00)
    {
        return SubcommandWithResponse(SubCommandOperation.SetPlayerLights, [leds]) != null;
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

        SubcommandWithResponse(SubCommandOperation.SetHomeLight, buf);
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

    private bool SetHCIState(byte state)
    {
        StopRumbleInSubcommands();
        return SubcommandWithResponse(SubCommandOperation.SetHCIState, [state]) != null;
    }

    private void SetMotion(bool enable)
    {
        if (!MotionSupported())
        {
            return;
        }

        SubcommandWithResponse(SubCommandOperation.EnableIMU, [enable ? (byte)0x01 : (byte)0x00]);
    }

    private void SetMotionSensitivity()
    {
        if (!MotionSupported())
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
        SubcommandWithResponse(SubCommandOperation.SetIMUSensitivity, buf);
    }

    private void SetRumble(bool enable)
    {
        SubcommandWithResponse(SubCommandOperation.EnableVibration, [enable ? (byte)0x01 : (byte)0x00]);
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

        SubcommandWithResponse(SubCommandOperation.SetMCUState, [enable ? (byte)0x01 : (byte)0x00]);
    }

    private bool SetReportMode(InputReportMode reportMode, bool checkResponse = true)
    {
        if (checkResponse)
        {
            return SubcommandWithResponse(SubCommandOperation.SetReportMode, [(byte)reportMode]) != null;
        }

        Subcommand(SubCommandOperation.SetReportMode, [(byte)reportMode]);
        return true;
    }

    private void CheckIfRightIsRetro()
    {
        SubCommandReturnPacket? response;

        for (var i = 0; i < 5; ++i)
        {
            response = SubcommandWithResponse(SubCommandOperation.RequestDeviceInfo, [], false); //Uses response

            if (response == null)
            {
                continue;
            }

            // The NES and Famicom controllers both share the hardware id of a normal right joycon.
            // To identify them, we need to query the hardware directly.
            // NES Left: 0x09
            // NES Right: 0x0A
            // Famicom I (Left): 0x07
            // Famicom II (Right): 0x08
            var deviceType = response.Payload[2];

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

        throw new DeviceComFailedException("reset device info");
    }

    private void SetLowPowerState(bool enable)
    {
        SubcommandWithResponse(SubCommandOperation.EnableLowPowerMode, [enable ? (byte)0x01 : (byte)0x00]);
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
        if (!IsDeviceReady)
        {
            return false;
        }

        Log("Powering off.");

        if (!SetHCIState(0x00) && _device.GetErrorCode() != (int)HIDApi.Device.ErrorCode.DeviceNotConnected)
        {
            return false;
        }

        Drop(false, false);
        return true;
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
                //SetMotion(false);
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
            OutDs4.UpdateInput(MapToDualShock4Input());
            OutXbox.UpdateInput(MapToXbox360Input());
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
        var length = Read(buf);

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

        if (packetType != (byte)InputReportMode.StandardFull/* && packetType != (byte)InputReportMode.SimpleHID*/)
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
        const int NbMotionPackets = 3;

        ulong deltaPacketsMicroseconds = 0;
        byte packetType = buf[0];

        if (packetType == (byte)InputReportMode.StandardFull)
        {
            // Determine the motion timestamp with a rolling average instead of relying on the unreliable packet's timestamp
            // more detailed explanations on why : https://github.com/torvalds/linux/blob/52b1853b080a082ec3749c3a9577f6c71b1d4a90/drivers/hid/hid-nintendo.c#L1115
            if (_timeSinceReceive.IsRunning)
            {
                var deltaReceiveMs = _timeSinceReceive.ElapsedMilliseconds;
                _avgReceiveDeltaMs.AddValue((int)deltaReceiveMs);
            }
            _timeSinceReceive.Restart();

            var deltaPacketsMs = _avgReceiveDeltaMs.GetAverage() / NbMotionPackets;
            deltaPacketsMicroseconds = (ulong)(deltaPacketsMs * 1000);

            _AHRS.SamplePeriod = deltaPacketsMs / 1000;
        }

        GetMainAndOtherController(out Joycon mainController, out var _);

        try
        {
            // Only joycons support joining. Need to lock to synchronize inputs between two joycons
            if (IsJoycon)
            {
                mainController._updateInputLock.Enter();
            }

            GetBatteryInfos(buf);
            ProcessButtonsAndSticks(buf);
            CopyInputFromJoinedController();
            UpdateInputActivity();

            UdpControllerReport? controllerReport = Program.Server != null && Program.Server.HasClients
                    ? new UdpControllerReport(this, deltaPacketsMicroseconds)
                    : null;

            // Process packets as soon as they come
            for (var n = 0; n < NbMotionPackets; ++n)
            {
                if (!ExtractMotionValues(buf, n))
                {
                    Timestamp += deltaPacketsMicroseconds * NbMotionPackets;
                    PacketCounter++;

                    controllerReport?.ClearMotionAndDeltaPackets();
                    break;
                }

                Timestamp += deltaPacketsMicroseconds;
                PacketCounter++;

                controllerReport?.AddMotion(this, n);

                DoThingsWithMotion();
            }

            DoThingsWithButtons();

            mainController.UpdateInput();

            if (controllerReport != null)
            {
                // We add the input at the end to take the controller remapping into account
                controllerReport.AddInput(this);

                Program.Server!.SendControllerReport(controllerReport);
            }
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
            var isShaking = _motion.Accelerometer.LengthSquared() >= Config.ShakeSensitivity;
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
            var controller = !IsLeft ? this : Other!;

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

    private bool GetMainAndOtherController(out Joycon main, [NotNullWhen(true)] out Joycon? other)
    {
        (main, other) = (IsLeft || !IsJoined) ? (this, Other) : (Other, this);

        return IsJoined;
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
        if (Config.ChangeOrientationDoubleClick && IsJoycon && !_calibrateSticks && !_calibrateMotion)
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
            if (HandleJoyAction("reset_mouse", out button) &&
                IsButtonDown(button) &&
                Screen.PrimaryScreen is Screen primaryScreen)
            {
                WindowsInput.Simulate.Events()
                    .MoveTo(
                        primaryScreen.Bounds.Width / 2,
                        primaryScreen.Bounds.Height / 2
                    )
                    .Invoke();
            }
        }
    }

    // Must be done by all controllers when their motion is updated (in the case they are joined)
    private void DoThingsWithMotionEachController()
    {
        // Filtered motion data
        _AHRS.GetEulerAngles(_curRotation);

        DetectShake();

        if (UseGyroAnalogSliders())
        {
            int dy;

            if (Config.UseFilteredMotion)
            {
                dy = (int)(Config.GyroAnalogSensitivity * (_curRotation[0] - _curRotation[3]));
            }
            else
            {
                float dt = _AHRS.SamplePeriod;
                dy = (int)(Config.GyroAnalogSensitivity * (_motion.Gyroscope.Y * dt));
            }

            if (_buttons[(int)Button.Shoulder2])
            {
                _sliderVal[IsLeft ? 0 : 1] = (byte)Math.Clamp(_sliderVal[IsLeft ? 0 : 1] + dy, 0, byte.MaxValue);
            }
            else
            {
                _sliderVal[IsLeft ? 0 : 1] = 0;
            }

            if (!IsJoycon)
            {
                if (_buttons[(int)Button.Shoulder22])
                {
                    _sliderVal[1] = (byte)Math.Clamp(_sliderVal[1] + dy, 0, byte.MaxValue);
                }
                else
                {
                    _sliderVal[1] = 0;
                }
            }
            else
            {
                _sliderVal[IsLeft ? 1 : 0] = 0;
            }

            if (GetMainAndOtherController(out Joycon mainController, out Joycon? otherController))
            {
                mainController._sliderVal[1] = otherController._sliderVal[1];
            }
        }

        if (IsPrimaryGyro)
        {
            float dt = _AHRS.SamplePeriod;

            if (Config.ExtraGyroFeature.StartsWith("joy"))
            {
                if (Settings.Value("active_gyro") == "0" || ActiveGyro)
                {
                    GetMainAndOtherController(out Joycon mainController, out var _);
                    ref var controlStick = ref (Config.ExtraGyroFeature == "joy_left" ? ref mainController._stick : ref mainController._stick2);

                    float dx, dy;
                    if (Config.UseFilteredMotion)
                    {
                        dx = Config.GyroStickSensitivity[0] * (_curRotation[1] - _curRotation[4]); // yaw
                        dy = -(Config.GyroStickSensitivity[1] * (_curRotation[0] - _curRotation[3])); // pitch
                    }
                    else
                    {
                        dx = Config.GyroStickSensitivity[0] * (_motion.Gyroscope.Z * dt); // yaw
                        dy = -(Config.GyroStickSensitivity[1] * (_motion.Gyroscope.Y * dt)); // pitch
                    }

                    controlStick.X = Math.Clamp(controlStick.X / Config.GyroStickReduction + dx, -1.0f, 1.0f);
                    controlStick.Y = Math.Clamp(controlStick.Y / Config.GyroStickReduction + dy, -1.0f, 1.0f);
                }
            }
            else if (Config.ExtraGyroFeature == "mouse")
            {
                // gyro data is in degrees/s
                if (Settings.Value("active_gyro") == "0" || ActiveGyro)
                {
                    int dx, dy;

                    if (Config.UseFilteredMotion)
                    {
                        dx = (int)(Config.GyroMouseSensitivity[0] * (_curRotation[1] - _curRotation[4])); // yaw
                        dy = (int)-(Config.GyroMouseSensitivity[1] * (_curRotation[0] - _curRotation[3])); // pitch
                    }
                    else
                    {
                        dx = (int)(Config.GyroMouseSensitivity[0] * (_motion.Gyroscope.Z * dt));
                        dy = (int)-(Config.GyroMouseSensitivity[1] * (_motion.Gyroscope.Y * dt));
                    }

                    WindowsInput.Simulate.Events().MoveBy(dx, dy).Invoke();
                }
            }
        }
    }

    private void DoThingsWithButtons()
    {
        // Updating a controller's button impacts the joined controller
        if (GetMainAndOtherController(out Joycon mainController, out Joycon? otherController))
        {
            otherController.DoThingsWithButtonsEachController();
        }

        mainController.DoThingsWithButtonsEachController();
        mainController.DoThingsWithButtonsMainController();
    }

    private void DoThingsWithMotion()
    {
        DoThingsWithMotionEachController();
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

        byte highNibble = BitWrangler.UpperNibble(reportBuf[2]);
        Battery = BitWrangler.ByteToEnumOrDefault(highNibble, BatteryLevel.Unknown, 0x0E);
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
        const int SendRumbleIntervalMs = 50;

        // the home light stays on for 2625ms, set to less than half in case of packet drop
        const int SendHomeLightIntervalMs = 1250;

        Stopwatch timeSinceRumble = new();
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

            var sendRumble = false;

            if (!timeSinceRumble.IsRunning || timeSinceRumble.ElapsedMilliseconds > SendRumbleIntervalMs)
            {
                if (_rumbles.TryDequeue(_rumbleBuf))
                {
                    sendRumble = true;
                    timeSinceRumble.Restart();
                }
            }

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

        // For motion timestamp calculation
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
                        State = Status.MotionDataOk;
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

    private static ushort Scale16bitsTo12bits(ushort value)
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

            _stickPrecal.X = BitWrangler.Lower3NibblesLittleEndian(reportBuf[6 + offset], reportBuf[7 + offset]);
            _stickPrecal.Y = BitWrangler.Upper3NibblesLittleEndian(reportBuf[7 + offset], reportBuf[8 + offset]);

            if (IsPro)
            {
                _stick2Precal.X = BitWrangler.Lower3NibblesLittleEndian(reportBuf[9], reportBuf[10]);
                _stick2Precal.Y = BitWrangler.Upper3NibblesLittleEndian(reportBuf[10], reportBuf[11]);
            }
        }
        else if (reportType == (byte)InputReportMode.SimpleHID)
        {
            if (IsPro)
            {
                // Scale down to 12 bits to match the calibrations datas precision
                // Invert y axis by substracting from 0xFFFF to match 0x30 reports 
                _stickPrecal.X = Scale16bitsTo12bits(BitWrangler.EncodeBytesAsWordLittleEndian(reportBuf[4], reportBuf[5]));
                _stickPrecal.Y = Scale16bitsTo12bits(BitWrangler.InvertWord(BitWrangler.EncodeBytesAsWordLittleEndian(reportBuf[6], reportBuf[7])));

                _stick2Precal.X = Scale16bitsTo12bits(BitWrangler.EncodeBytesAsWordLittleEndian(reportBuf[8], reportBuf[9]));
                _stick2Precal.Y = Scale16bitsTo12bits(BitWrangler.InvertWord(BitWrangler.EncodeBytesAsWordLittleEndian(reportBuf[10], (reportBuf[11]))));
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
                    case 0x00: offsetY = _stickCal.YMax; break; // top
                    case 0x01: offsetX = _stickCal.XMax; offsetY = _stickCal.YMax; break; // top right
                    case 0x02: offsetX = _stickCal.XMax; break; // right
                    case 0x03: offsetX = _stickCal.XMax; offsetY = -_stickCal.YMin; break; // bottom right
                    case 0x04: offsetY = -_stickCal.YMin; break; // bottom
                    case 0x05: offsetX = -_stickCal.XMin; offsetY = -_stickCal.YMin; break; // bottom left
                    case 0x06: offsetX = -_stickCal.XMin; break; // left
                    case 0x07: offsetX = -_stickCal.XMin; offsetY = _stickCal.YMax; break; // top left
                    case 0x08: default: break; // center
                }

                _stickPrecal.X = (ushort)(_stickCal.XCenter + offsetX);
                _stickPrecal.Y = (ushort)(_stickCal.YCenter + offsetY);
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
            var dz = _deadZone;
            var range = _range;
            var antiDeadzone = Config.StickLeftAntiDeadzone;

            if (_SticksCalibrated)
            {
                cal = _activeStick1;
                dz = StickDeadZoneCalibration.FromConfigLeft(Config);
                range = StickRangeCalibration.FromConfigLeft(Config);
            }

            CalculateStickCenter(_stickPrecal, cal, dz, range, antiDeadzone, ref _stick);

            if (IsPro)
            {
                cal = _stick2Cal;
                dz = _deadZone2;
                range = _range2;
                antiDeadzone = Config.StickRightAntiDeadzone;

                if (_SticksCalibrated)
                {
                    cal = _activeStick2;
                    dz = StickDeadZoneCalibration.FromConfigRight(Config);
                    range = StickRangeCalibration.FromConfigRight(Config);
                }

                CalculateStickCenter(_stick2Precal, cal, dz, range, antiDeadzone, ref _stick2);
            }
            // Read other Joycon's sticks
            else
            {
                _stick2 = Stick.Zero;
            }

            if (_calibrateSticks)
            {
                var sticks = new SticksData(_stickPrecal, _stick2Precal);
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
        if (!GetMainAndOtherController(out Joycon mainController, out Joycon? otherController))
        {
            return;
        }

        mainController._buttons[(int)Button.B] = otherController._buttons[(int)Button.DpadDown];
        mainController._buttons[(int)Button.A] = otherController._buttons[(int)Button.DpadRight];
        mainController._buttons[(int)Button.X] = otherController._buttons[(int)Button.DpadUp];
        mainController._buttons[(int)Button.Y] = otherController._buttons[(int)Button.DpadLeft];

        mainController._buttons[(int)Button.Stick2] = otherController._buttons[(int)Button.Stick];
        mainController._buttons[(int)Button.Shoulder21] = otherController._buttons[(int)Button.Shoulder1];
        mainController._buttons[(int)Button.Shoulder22] = otherController._buttons[(int)Button.Shoulder2];

        mainController._buttons[(int)Button.Home] = otherController._buttons[(int)Button.Home];
        mainController._buttons[(int)Button.Plus] = otherController._buttons[(int)Button.Plus];

        mainController._stick2 = otherController._stick;
    }

    // Must be done by all controllers when their input is updated (in the case they are joined)
    private void UpdateInputActivityEachController()
    {
        var activity = false;
        var timestamp = Stopwatch.GetTimestamp();

        if (SticksSupported())
        {
            const float StickActivityThreshold = 0.1f;
            if (MathF.Abs(_stick.X) > StickActivityThreshold ||
                MathF.Abs(_stick.Y) > StickActivityThreshold ||
                MathF.Abs(_stick2.X) > StickActivityThreshold ||
                MathF.Abs(_stick2.Y) > StickActivityThreshold)
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
        if (!GetMainAndOtherController(out Joycon mainController, out Joycon? otherController))
        {
            mainController.UpdateInputActivityEachController();
            return;
        }

        // Need to update both joined controllers in case they are split afterward
        mainController.UpdateInputActivityEachController();
        otherController.UpdateInputActivityEachController();

        // Consider the other joined controller active when the main controller is (so it doesn't power off after splitting)
        var mostRecentActivity = Math.Max(mainController._timestampActivity, otherController._timestampActivity);
        mainController._timestampActivity = mostRecentActivity;
        otherController._timestampActivity = mostRecentActivity;
    }

    private static long TimestampToMs(long timestamp)
    {
        long ticksPerMillisecond = Stopwatch.Frequency / 1000;
        return timestamp / ticksPerMillisecond;
    }

    // Get Gyro/Accel data
    private bool ExtractMotionValues(ReadOnlySpan<byte> reportBuf, int n = 0)
    {
        if (!MotionSupported() || reportBuf[0] != (byte)InputReportMode.StandardFull)
        {
            return false;
        }

        var offset = n * 12;

        MotionShort motionRaw = new(
            gyroscope: new(
                X: BitWrangler.EncodeBytesAsWordLittleEndianSigned(reportBuf[19 + offset], reportBuf[20 + offset]),
                Y: BitWrangler.EncodeBytesAsWordLittleEndianSigned(reportBuf[21 + offset], reportBuf[22 + offset]),
                Z: BitWrangler.EncodeBytesAsWordLittleEndianSigned(reportBuf[23 + offset], reportBuf[24 + offset])
            ),
            accelerometer: new(
                X: BitWrangler.EncodeBytesAsWordLittleEndianSigned(reportBuf[13 + offset], reportBuf[14 + offset]),
                Y: BitWrangler.EncodeBytesAsWordLittleEndianSigned(reportBuf[15 + offset], reportBuf[16 + offset]),
                Z: BitWrangler.EncodeBytesAsWordLittleEndianSigned(reportBuf[17 + offset], reportBuf[18 + offset])
            )
        );

        if (_calibrateMotion)
        {
            // We need to add the accelerometer offset from the origin position when it's on a flat surface
            ThreeAxisShort accOffset;
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

            var motionData = new MotionShort(motionRaw.Gyroscope, motionRaw.Accelerometer - accOffset);
            CalibrationMotionDatas.Add(motionData);
        }

        var direction = IsLeft ? 1 : -1;

        MotionShort neutral = _motionCalibrated
            ? new(
                accelerometer: new(
                    X: _activeMotionData[3],
                    Y: _activeMotionData[4],
                    Z: _activeMotionData[5]
                ),
                gyroscope: new(
                    X: _activeMotionData[0],
                    Y: _activeMotionData[1],
                    Z: _activeMotionData[2]
                )
            )
            : new(
                // Don't use neutral position with factory calibration for the accelerometer, it's more accurate
                accelerometer: ThreeAxisShort.Zero,
                gyroscope: _motionCalibration.GyroscopeNeutral
            );

        _motion.Accelerometer.X = (motionRaw.Accelerometer.X - neutral.Accelerometer.X) * (1.0f / (_motionCalibration.AccelerometerSensitivity.X - _motionCalibration.AccelerometerNeutral.X)) * 4.0f;
        _motion.Accelerometer.Y = direction * (motionRaw.Accelerometer.Y - neutral.Accelerometer.Y) * (1.0f / (_motionCalibration.AccelerometerSensitivity.Y - _motionCalibration.AccelerometerNeutral.Y)) * 4.0f;
        _motion.Accelerometer.Z = direction * (motionRaw.Accelerometer.Z - neutral.Accelerometer.Z) * (1.0f / (_motionCalibration.AccelerometerSensitivity.Z - _motionCalibration.AccelerometerNeutral.Z)) * 4.0f;

        _motion.Gyroscope.X = (motionRaw.Gyroscope.X - neutral.Gyroscope.X) * (816.0f / (_motionCalibration.GyroscopeSensitivity.X - neutral.Gyroscope.X));
        _motion.Gyroscope.Y = -direction * (motionRaw.Gyroscope.Y - neutral.Gyroscope.Y) * (816.0f / (_motionCalibration.GyroscopeSensitivity.Y - neutral.Gyroscope.Y));
        _motion.Gyroscope.Z = -direction * (motionRaw.Gyroscope.Z - neutral.Gyroscope.Z) * (816.0f / (_motionCalibration.GyroscopeSensitivity.Z - neutral.Gyroscope.Z));

        if (IsJoycon && Other == null)
        {
            // single joycon mode; Z do not swap, rest do
            if (IsLeft)
            {
                _motion.Accelerometer.X = -_motion.Accelerometer.X;
                _motion.Accelerometer.Y = -_motion.Accelerometer.Y;
                _motion.Gyroscope.X = -_motion.Gyroscope.X;
            }
            else
            {
                _motion.Gyroscope.Y = -_motion.Gyroscope.Y;
            }

            var temp = _motion.Accelerometer.X;
            _motion.Accelerometer.X = _motion.Accelerometer.Y;
            _motion.Accelerometer.Y = -temp;

            temp = _motion.Gyroscope.X;
            _motion.Gyroscope.X = _motion.Gyroscope.Y;
            _motion.Gyroscope.Y = temp;
        }

        // Update rotation Quaternion
        var degToRad = 0.0174533f;
        _AHRS.Update(
            _motion.Gyroscope.X * degToRad,
            _motion.Gyroscope.Y * degToRad,
            _motion.Gyroscope.Z * degToRad,
            _motion.Accelerometer.X,
            _motion.Accelerometer.Y,
            _motion.Accelerometer.Z
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

    private void CalculateStickCenter(TwoAxisUShort vals, StickLimitsCalibration cal, float deadzone, float range, float[] antiDeadzone, ref Stick stick)
    {
        float dx = vals.X - cal.XCenter;
        float dy = vals.Y - cal.YCenter;

        float normalizedX = dx / (dx > 0 ? cal.XMax : cal.XMin);
        float normalizedY = dy / (dy > 0 ? cal.YMax : cal.YMin);

        float magnitude = MathF.Sqrt(normalizedX * normalizedX + normalizedY * normalizedY);

        if (magnitude <= deadzone || range <= deadzone)
        {
            // Inner deadzone
            stick.X = 0.0f;
            stick.Y = 0.0f;
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
                stick.X = normalizedX;
                stick.Y = normalizedY;
            }
            else
            {
                // Expand the circle to a square area
                if (Math.Abs(normalizedX) > Math.Abs(normalizedY))
                {
                    stick.X = Math.Sign(normalizedX) * normalizedMagnitudeX;
                    stick.Y = stick.X * normalizedY / normalizedX;
                }
                else
                {
                    stick.Y = Math.Sign(normalizedY) * normalizedMagnitudeY;
                    stick.X = stick.Y * normalizedX / normalizedY;
                }
            }

            stick.X = Math.Clamp(stick.X, -1.0f, 1.0f);
            stick.Y = Math.Clamp(stick.Y, -1.0f, 1.0f);
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

    private SubCommandReturnPacket? SubcommandWithResponse(
        SubCommandOperation operation,
        ReadOnlySpan<byte> bufParameters,
        bool print = true)
    {
        Span<byte> responseBuf = stackalloc byte[ReportLength];
        SubCommandReturnPacket? response = null;
        int length = Subcommand(operation, bufParameters, print);

        if (length <= 0)
        {
            DebugPrint($"Subcommand write error: {(length == 0 ? "No data written." : ErrorMessage())}", DebugType.Comms);

            return null;
        }

        for (int tries = 0; tries < 10; tries++)
        {
            length = Read(responseBuf); //Returns < 1 on error, 0 on timeout

            if (length < 0)
            {
                DebugPrint($"Subcommand read error: {ErrorMessage()}", DebugType.Comms);

                return null;
            }

            if (SubCommandReturnPacket.TryConstruct(operation, responseBuf[..length], out response))
            {
                if (print)
                {
                    DebugPrint(response, DebugType.Comms);
                }

                return response;
            }
        }

        DebugPrint("No response.", DebugType.Comms);

        return response;
    }

    private bool CalibrationDataSupported()
    {
        return !IsThirdParty && (IsJoycon || IsPro || IsN64);
    }

    private bool SticksSupported()
    {
        return IsJoycon || IsPro || IsN64;
    }

    public bool MotionSupported()
    {
        return IsJoycon || IsPro;
    }

    public bool HomeLightSupported()
    {
        return Type == ControllerType.JoyconRight || IsPro;
    }

    private bool UseGyroAnalogSliders()
    {
        return Config.GyroAnalogSliders && MotionSupported() && (!IsJoycon || Other != null);
    }

    private bool DumpCalibrationData()
    {
        if (!CalibrationDataSupported())
        {
            _deadZone = StickDeadZoneCalibration.FromConfigLeft(Config);
            _deadZone2 = StickDeadZoneCalibration.FromConfigRight(Config);

            _range = StickRangeCalibration.FromConfigLeft(Config);
            _range2 = StickRangeCalibration.FromConfigRight(Config);

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

            _stickCal = IsLeft ?
                StickLimitsCalibration.FromLeftStickCalibrationBytes(stick1Data) :
                StickLimitsCalibration.FromRightStickCalibrationBytes(stick1Data);

            DebugPrint(_stickCal, DebugType.None);

            if (IsPro) //If it is pro, then it is also always left
            {
                var stick2Data = new ReadOnlySpan<byte>(userStickData, 13, 9);
                var stick2Name = "right";

                if (ok)
                {
                    if (userStickData[11] == 0xB2 && userStickData[12] == 0xA1)
                    {
                        DebugPrint($"Retrieve user {stick2Name} stick calibration data.", DebugType.Comms);
                    }
                    else
                    {
                        stick2Data = new ReadOnlySpan<byte>(factoryStickData, 9, 9);

                        DebugPrint($"Retrieve factory {stick2Name} stick calibration data.", DebugType.Comms);
                    }
                }

                _stick2Cal = StickLimitsCalibration.FromRightStickCalibrationBytes(stick2Data);

                DebugPrint(_stick2Cal, DebugType.None);
            }
        }

        // Sticks deadzones and ranges
        // Looks like the range is a 12 bits precision ratio.
        // I suppose the right way to interpret it is as a float by dividing it by 0xFFF
        {
            var factoryDeadzoneData = ReadSPICheck(SPIPage.StickDeadZone, ref ok);

            var offset = IsLeft ? 0 : 0x12;

            _deadZone = new StickDeadZoneCalibration(_stickCal, factoryDeadzoneData.AsSpan(offset, 2));
            _range = new StickRangeCalibration(factoryDeadzoneData.AsSpan(offset + 1, 2));

            if (IsPro) //If it is pro, then it is also always left
            {
                offset = 0x12;

                _deadZone2 = new StickDeadZoneCalibration(_stickCal, factoryDeadzoneData.AsSpan(offset, 2));
                _range2 = new StickRangeCalibration(factoryDeadzoneData.AsSpan(offset + 1, 2));
            }
        }

        // Gyro and accelerometer
        if (MotionSupported())
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

            _motionCalibration = new MotionCalibration(sensorData);

            if (_motionCalibration.UsedDefaultValues)
            {
                Log("Some sensor calibrations datas are missing, fallback to default ones.", Logger.LogLevel.Warning);
            }

            DebugPrint(_motionCalibration, DebugType.Motion);
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
            GetActiveMotionData();
            GetActiveSticksData();
        }
        else
        {
            _motionCalibrated = false;
            _SticksCalibrated = false;
        }

        var calibrationType = _SticksCalibrated ? "user" : _DumpedCalibration ? "controller" : "default";
        Log($"Using {calibrationType} sticks calibration.");

        if (MotionSupported())
        {
            calibrationType = _motionCalibrated ? "user" : _DumpedCalibration ? "controller" : "default";
            Log($"Using {calibrationType} sensors calibration.");
        }
    }

    private int Read(Span<byte> response)
    {
        if (response.Length < ReportLength)
        {
            throw new IndexOutOfRangeException();
        }

        if (IsDeviceError)
        {
            return DeviceErroredCode;
        }

        return _device.ReadTimeout(response, ReportLength, 100); // don't set the timeout lower than 100 or might not always work
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
            length = Read(response);
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

        SubCommandReturnPacket? response;

        ok = false;
        for (var i = 0; i < 5; ++i)
        {
            response = SubcommandWithResponse(SubCommandOperation.SPIFlashRead, page, false); //Uses response
            if (response != null &&
                response.Length >= SubCommandReturnPacket.MinimumSubcommandReplySize + page.PageSize &&
                response.Payload[0] == page.LowAddress &&
                response.Payload[1] == page.HighAddress)
            {
                ok = true;

                if (print)
                {
                    PrintArray<byte>(readBuf.AsSpan(0, page.PageSize), DebugType.Comms);
                }

                response.Payload.Slice(5, page.PageSize).CopyTo(readBuf);

                return readBuf;
            }
        }

        Log("ReadSPI error.", Logger.LogLevel.Error);

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

    private OutputControllerXbox360InputState MapToXbox360Input()
    {
        var output = new OutputControllerXbox360InputState();

        var isN64 = IsN64;
        var isJoycon = IsJoycon;
        var isLeft = IsLeft;
        var other = Other;

        var buttons = _buttonsRemapped;
        var stick = _stick;
        var stick2 = _stick2;
        var sliderVal = _sliderVal;

        var gyroAnalogSliders = UseGyroAnalogSliders();
        var swapAB = Config.SwapAB;
        var swapXY = Config.SwapXY;

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

        if (SticksSupported())
        {
            if (isJoycon && other == null)
            {
                output.AxisLeftY = CastStickValue((isLeft ? 1 : -1) * stick.X);
                output.AxisLeftX = CastStickValue((isLeft ? -1 : 1) * stick.Y);
            }
            else if (isN64)
            {
                output.AxisLeftX = CastStickValue(stick.X);
                output.AxisLeftY = CastStickValue(stick.Y);

                // C buttons mapped to right stick
                output.AxisRightX = CastStickValue((buttons[(int)Button.X] ? -1 : 0) + (buttons[(int)Button.Minus] ? 1 : 0));
                output.AxisRightY = CastStickValue((buttons[(int)Button.Shoulder22] ? -1 : 0) + (buttons[(int)Button.Y] ? 1 : 0));
            }
            else
            {
                output.AxisLeftX = CastStickValue(other == this && !isLeft ? stick2.X : stick.X);
                output.AxisLeftY = CastStickValue(other == this && !isLeft ? stick2.Y : stick.Y);

                output.AxisRightX = CastStickValue(other == this && !isLeft ? stick.X : stick2.X);
                output.AxisRightY = CastStickValue(other == this && !isLeft ? stick.Y : stick2.Y);
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

    public OutputControllerDualShock4InputState MapToDualShock4Input()
    {
        var output = new OutputControllerDualShock4InputState();

        var isN64 = IsN64;
        var isJoycon = IsJoycon;
        var isLeft = IsLeft;
        var other = Other;

        var buttons = _buttonsRemapped;
        var stick = _stick;
        var stick2 = _stick2;
        var sliderVal = _sliderVal;

        var gyroAnalogSliders = UseGyroAnalogSliders();
        var swapAB = Config.SwapAB;
        var swapXY = Config.SwapXY;

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

        if (SticksSupported())
        {
            if (isJoycon && other == null)
            {
                output.ThumbLeftY = CastStickValueByte((isLeft ? 1 : -1) * -stick.X);
                output.ThumbLeftX = CastStickValueByte((isLeft ? 1 : -1) * -stick.Y);

                output.ThumbRightX = CastStickValueByte(0);
                output.ThumbRightY = CastStickValueByte(0);
            }
            else if (isN64)
            {
                output.ThumbLeftX = CastStickValueByte(stick.X);
                output.ThumbLeftY = CastStickValueByte(-stick.Y);

                // C buttons mapped to right stick
                output.ThumbRightX = CastStickValueByte((buttons[(int)Button.X] ? -1 : 0) + (buttons[(int)Button.Minus] ? 1 : 0));
                output.ThumbRightY = CastStickValueByte((buttons[(int)Button.Shoulder22] ? 1 : 0) + (buttons[(int)Button.Y] ? -1 : 0));
            }
            else
            {
                output.ThumbLeftX = CastStickValueByte(other == this && !isLeft ? stick2.X : stick.X);
                output.ThumbLeftY = CastStickValueByte(other == this && !isLeft ? -stick2.Y : -stick.Y);

                output.ThumbRightX = CastStickValueByte(other == this && !isLeft ? stick.X : stick2.X);
                output.ThumbRightY = CastStickValueByte(other == this && !isLeft ? -stick.Y : -stick2.Y);

                //DebugPrint($"X:{-stick.X:0.00} Y:{stick.Y:0.00}", DebugType.Threading);
                //DebugPrint($"X:{output.ThumbLeftX} Y:{output.ThumbLeftY}", DebugType.Threading);
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
#pragma warning disable IDE0055 // Disable formatting
            ControllerType.JoyconLeft  => "Left joycon",
            ControllerType.JoyconRight => "Right joycon",
            ControllerType.Pro         => "Pro controller",
            ControllerType.SNES        => "SNES controller",
            ControllerType.NES         => "NES controller",
            ControllerType.FamicomI    => "Famicom I controller",
            ControllerType.FamicomII   => "Famicom II controller",
            ControllerType.N64         => "N64 controller",
            _                          => "Controller"
#pragma warning restore IDE0055
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

    public void StartMotionCalibration()
    {
        CalibrationMotionDatas.Clear();
        _calibrateMotion = true;
    }

    public void StopMotionCalibration(bool clean = false)
    {
        _calibrateMotion = false;

        if (clean)
        {
            CalibrationMotionDatas.Clear();
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
                _deadZone = StickDeadZoneCalibration.FromConfigLeft(Config);
            }

            if (oldConfig.StickRightDeadzone != Config.StickRightDeadzone)
            {
                _deadZone2 = StickDeadZoneCalibration.FromConfigRight(Config);
            }

            if (oldConfig.StickLeftRange != Config.StickLeftRange)
            {
                _range = StickRangeCalibration.FromConfigLeft(Config);
            }

            if (oldConfig.StickRightRange != Config.StickRightRange)
            {
                _range2 = StickRangeCalibration.FromConfigRight(Config);
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

public record struct SticksData(TwoAxisUShort Stick1, TwoAxisUShort Stick2);
