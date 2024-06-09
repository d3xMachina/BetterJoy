using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Numerics;
using System.Threading;
using System.Windows.Forms;
using BetterJoy.Collections;
using BetterJoy.Controller;
using BetterJoy.Exceptions;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using WindowsInput.Events;

namespace BetterJoy
{
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

        public enum ControllerType
        {
            JoyconLeft,
            JoyconRight,
            Pro,
            SNES
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
            Empty,
            Critical,
            Low,
            Medium,
            Full
        }

        private enum ReceiveError
        {
            None,
            InvalidHandle,
            ReadError,
            InvalidPacket,
            NoData
        }

        private enum ReportMode
        {
            StandardFull = 0x30,
            SimpleHID = 0x3F
        }

        private const int DeviceErroredCode = -100; // custom error

        private const int ReportLength = 49;
        private readonly int _CommandLength;
        private readonly int _MixedComsLength; // when the buffer is used for both read and write to hid

        public readonly ControllerConfig Config;

        private static readonly byte[] LedById = { 0b0001, 0b0011, 0b0111, 0b1111, 0b1001, 0b0101, 0b1101, 0b0110 };

        private readonly short[] _accNeutral = { 0, 0, 0 };
        private readonly short[] _accRaw = { 0, 0, 0 };
        private readonly short[] _accSensiti = { 0, 0, 0 };

        private readonly MadgwickAHRS _AHRS; // for getting filtered Euler angles of rotation; 5ms sampling rate

        private readonly bool[] _buttons = new bool[20];
        private readonly bool[] _buttonsDown = new bool[20];
        private readonly long[] _buttonsDownTimestamp = new long[20];
        private readonly bool[] _buttonsUp = new bool[20];
        private readonly bool[] _buttonsPrev = new bool[20];
        private readonly bool[] _buttonsRemapped = new bool[20];

        private readonly float[] _curRotation = { 0, 0, 0, 0, 0, 0 }; // Filtered IMU data

        private readonly byte[] _defaultBuf = { 0x0, 0x1, 0x40, 0x40, 0x0, 0x1, 0x40, 0x40 };

        private readonly short[] _gyrNeutral = { 0, 0, 0 };

        private readonly short[] _gyrRaw = { 0, 0, 0 };

        private readonly short[] _gyrSensiti = { 0, 0, 0 };

        private readonly bool _IMUEnabled;
        private readonly Dictionary<int, bool> _mouseToggleBtn = new();

        private readonly float[] _otherStick = { 0, 0 };

        // Values from https://github.com/dekuNukem/Nintendo_Switch_Reverse_Engineering/blob/master/spi_flash_notes.md#6-axis-horizontal-offsets
        private readonly short[] _accProHorOffset = { -688, 0, 4038 };
        private readonly short[] _accLeftHorOffset = { 350, 0, 4081 };
        private readonly short[] _accRightHorOffset = { 350, 0, -4081 };

        private readonly Stopwatch _shakeTimer = Stopwatch.StartNew(); //Setup a timer for measuring shake in milliseconds

        private readonly byte[] _sliderVal = { 0, 0 };

        private readonly ushort[] _stickCal = { 0, 0, 0, 0, 0, 0 };
        private readonly ushort[] _stickPrecal = { 0, 0 };

        private readonly ushort[] _stick2Cal = { 0, 0, 0, 0, 0, 0 };
        private readonly ushort[] _stick2Precal = { 0, 0 };

        private Vector3 _accG = Vector3.Zero;
        public bool ActiveGyro;

        private bool _DumpedCalibration = false;
        private bool _IMUCalibrated = false;
        private bool _SticksCalibrated = false;
        private readonly short[] _activeIMUData = new short[6];
        private readonly ushort[] _activeStick1 = new ushort[6];
        private readonly ushort[] _activeStick2 = new ushort[6];
        private float _activeStick1Deadzone;
        private float _activeStick2Deadzone;
        private float _activeStick1Range;
        private float _activeStick2Range;

        public BatteryLevel Battery = BatteryLevel.Empty;
        public bool Charging = false;

        private float _deadzone;
        private float _deadzone2;
        private float _range;
        private float _range2;
        
        private bool _doLocalize;

        private MainForm _form;

        private byte _globalCount;
        private Vector3 _gyrG = Vector3.Zero;

        private IntPtr _handle;
        private bool _hasShaked;

        public readonly bool IsThirdParty;
        public readonly bool IsUSB;
        private long _lastDoubleClick = -1;

        public OutputControllerDualShock4 OutDs4;
        public OutputControllerXbox360 OutXbox;
        private readonly object _updateInputLock = new object();

        public int PacketCounter;

        // For UdpServer
        public readonly int PadId;

        public PhysicalAddress PadMacAddress = new([01, 02, 03, 04, 05, 06]);
        public readonly string Path;

        private Thread _receiveReportsThread;
        private Thread _sendCommandsThread;

        private Rumble _rumbleObj;

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

        private float[] _stick = { 0, 0 };
        private float[] _stick2 = { 0, 0 };

        private bool _stopPolling = true;
        public ulong Timestamp;

        private long _timestampActivity = Stopwatch.GetTimestamp();

        public readonly ControllerType Type;

        public EventHandler<StateChangedEventArgs> StateChanged;

        public readonly ConcurrentList<IMUData> CalibrationIMUDatas = new();
        public readonly ConcurrentList<SticksData> CalibrationStickDatas = new();
        private bool _calibrateSticks = false;
        private bool _calibrateIMU = false;

        public readonly ReaderWriterLockSlim HidapiLock = new ReaderWriterLockSlim();

        private Stopwatch _timeSinceReceive = new();
        private RollingAverage _avgReceiveDeltaMs = new(100); // delta is around 10-16ms, so rolling average over 1000-1600ms 

        public Joycon(
            MainForm form,
            IntPtr handle,
            bool imu,
            bool localize,
            string path,
            string serialNum,
            bool isUSB,
            int id,
            ControllerType type,
            bool isThirdParty = false
        )
        {
            _form = form;

            Config = new(_form);
            Config.Update();

            SerialNumber = serialNum;
            SerialOrMac = serialNum;
            _handle = handle;
            _IMUEnabled = imu;
            _doLocalize = localize;
            _rumbleObj = new Rumble([Config.LowFreq, Config.HighFreq, 0]);
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
        }

        public bool IsPro => Type is ControllerType.Pro or ControllerType.SNES;
        public bool IsSNES => Type == ControllerType.SNES;
        public bool IsJoycon => Type is ControllerType.JoyconRight or ControllerType.JoyconLeft;
        public bool IsLeft => Type != ControllerType.JoyconRight;
        public bool IsJoined => Other != null && Other != this;
        public bool IsPrimaryGyro => !IsJoined || Config.GyroLeftHanded == IsLeft;

        public bool IsDeviceReady => State > Status.Dropped;
        public bool IsDeviceError => !IsDeviceReady && State != Status.NotAttached;


        public Joycon Other;

        public void SetLEDByPlayerNum(int id)
        {
            if (id >= LedById.Length)
            {
                // No support for any higher than 8 controllers
                id = LedById.Length - 1;
            }

            byte led = LedById[id];

            SetPlayerLED(led);
        }

        public void SetLEDByPadID()
        {
            if (!IsJoined)
            {
                // Set LED to current Pad ID
                SetLEDByPlayerNum(PadId);
            }
            else
            {
                // Set LED to current Joycon Pair
                var lowestPadId = Math.Min(Other.PadId, PadId);
                SetLEDByPlayerNum(lowestPadId);
            }
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
            _activeStick1Deadzone = Config.DefaultDeadzone;
            _activeStick2Deadzone = Config.DefaultDeadzone;

            _activeStick1Range = Config.DefaultRange;
            _activeStick2Range = Config.DefaultRange;

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
            SetRumble(Config.LowFreq, Config.HighFreq, Math.Max(e.LargeMotor, e.SmallMotor) / 255f);

            if (IsJoined)
            {
                Other.SetRumble(Config.LowFreq, Config.HighFreq, Math.Max(e.LargeMotor, e.SmallMotor) / 255f);
            }
        }

        public void Ds4_FeedbackReceived(DualShock4FeedbackReceivedEventArgs e)
        {
            if (!Config.EnableRumble)
            {
                return;
            }

            DebugPrint("Rumble data Received: DS4", DebugType.Rumble);
            SetRumble(Config.LowFreq, Config.HighFreq, Math.Max(e.LargeMotor, e.SmallMotor) / 255f);

            if (IsJoined)
            {
                Other.SetRumble(Config.LowFreq, Config.HighFreq, Math.Max(e.LargeMotor, e.SmallMotor) / 255f);
            }
        }

        private void OnStateChange(StateChangedEventArgs e)
        {
            StateChanged?.Invoke(this, e);
        }

        public void DebugPrint(string message, DebugType type)
        {
            if (Config.DebugType == DebugType.None)
            {
                return;
            }

            if (type == DebugType.All || type == Config.DebugType || Config.DebugType == DebugType.All)
            {
                Log(message);
            }
        }

        public Vector3 GetGyro()
        {
            return _gyrG;
        }

        public Vector3 GetAccel()
        {
            return _accG;
        }

        public void Reset()
        {
            Log("Resetting connection.");
            SetHCIState(0x01);
        }

        public void Attach()
        {
            if (IsDeviceReady)
            {
                return;
            }

            try
            {
                if (_handle == IntPtr.Zero)
                {
                    throw new DeviceNullHandleException("reset hidapi");
                }

                // set report mode to simple HID mode (fix SPI read not working when controller is already initialized)
                // do not always send a response so we don't check if there is one
                SetReportMode(ReportMode.SimpleHID);

                // Connect
                if (IsUSB)
                {
                    Log("Using USB.");

                    try
                    {
                        GetMAC();
                        USBPairing();
                    }
                    catch
                    {
                        Reset();
                        throw;
                    }

                    //BTManualPairing();
                }
                else
                {
                    Log("Using Bluetooth.");
                    GetMAC();
                }

                var ok = DumpCalibrationData();
                if (!ok)
                {
                    Reset();
                    throw new DeviceComFailedException("reset calibration");
                }

                BlinkHomeLight();
                SetLEDByPlayerNum(PadId);

                SetIMU(_IMUEnabled);
                SetRumble(true);
                SetReportMode(ReportMode.StandardFull);

                State = Status.Attached;

                DebugPrint("Done with init.", DebugType.Comms);
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
            catch { } // could not parse mac address, ignore

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
            SubcommandCheck(0x01, [0x01, btmac_host[5], btmac_host[4], btmac_host[3], btmac_host[2], btmac_host[1], btmac_host[0]], buf);
            SubcommandCheck(0x01, [0x02], buf); // LTKhash
            SubcommandCheck(0x01, [0x03], buf); // save pairing info
        }

        public void SetPlayerLED(byte leds = 0x00)
        {
            SubcommandCheck(0x30, [leds]);
        }

        public void BlinkHomeLight()
        {
            // do not call after initial setup
            if (IsThirdParty || Type == ControllerType.JoyconLeft)
            {
                return;
            }

            const byte intensity = 0x1;

            Span<byte> buf =
            [
                // Global settings
                0x18,
                0x01,

                // Mini cycle 1
                intensity << 4,
                0xFF,
                0xFF,
            ];
            SubcommandCheck(0x38, buf);
        }

        public void SetHomeLight(bool on)
        {
            if (IsThirdParty || Type == ControllerType.JoyconLeft)
            {
                return;
            }

            byte intensity = (byte)(on ? 0x1 : 0x0);
            const byte nbCycles = 0xF; // 0x0 for permanent light

            Span<byte> buf =
            [
                // Global settings
                0x0F, // 0XF = 175ms base duration
                (byte)(intensity << 4 | nbCycles),

                // Mini cycle 1
                // Somehow still used when buf[0] high nibble is set to 0x0
                // Increase the multipliers (like 0xFF instead of 0x11) to increase the duration beyond 2625ms
                (byte)(intensity << 4), // intensity | not used
                0x11, // transition multiplier | duration multiplier, both use the base duration
                0xFF, // not used
            ];
            Subcommand(0x38, buf); // don't wait for response
        }

        private void SetHCIState(byte state)
        {
            SubcommandCheck(0x06, [state]);
        }

        private void SetIMU(bool enable)
        {
            SubcommandCheck(0x40, [enable ? (byte)0x01 : (byte)0x00]);
        }

        private void SetRumble(bool enable)
        {
            SubcommandCheck(0x48, [enable ? (byte)0x01 : (byte)0x00]);
        }

        private void SetReportMode(ReportMode reportMode, bool checkResponse = true)
        {
            if (checkResponse)
            {
                SubcommandCheck(0x03, [(byte)reportMode]);
                return;
            }
            Subcommand(0x03, [(byte)reportMode]);
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

        public void PowerOff()
        {
            if (IsDeviceReady)
            {
                SetHCIState(0x00);
                State = Status.Dropped;
            }
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

        public void Detach(bool close = true)
        {
            if (State == Status.NotAttached)
            {
                return;
            }

            _stopPolling = true;
            _receiveReportsThread?.Join();
            _sendCommandsThread?.Join();

            DisconnectViGEm();

            if (_handle != IntPtr.Zero)
            {
                if (State > Status.Dropped)
                {
                    //SetIMU(false);
                    //SetRumble(false);
                    SetReportMode(ReportMode.SimpleHID);
                    SetPlayerLED(0);

                    // Commented because you need to restart the controller to reconnect in usb again with the following
                    //BTActivate();
                }

                if (close)
                {
                    HidapiLock.EnterWriteLock();
                    try
                    {
                        HIDApi.Close(_handle);
                        _handle = IntPtr.Zero;
                    }
                    finally
                    {
                        HidapiLock.ExitWriteLock();
                    }
                }
            }

            State = Status.NotAttached;
        }

        public void Drop(bool error = false)
        {
            _stopPolling = true;
            _receiveReportsThread?.Join();
            _sendCommandsThread?.Join();

            State = error ? Status.Errored : Status.Dropped;
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
            bool lockTaken = false;
            bool otherLockTaken = false;

            if (Type == ControllerType.JoyconLeft)
            {
                Monitor.Enter(_updateInputLock, ref lockTaken); // need with joined joycons
            }

            try
            {
                ref var ds4 = ref OutDs4;
                ref var xbox = ref OutXbox;

                // Update the left joycon virtual controller when joined
                if (!IsLeft && IsJoined)
                {
                    Monitor.Enter(Other._updateInputLock, ref otherLockTaken);

                    ds4 = ref Other.OutDs4;
                    xbox = ref Other.OutXbox;
                }

                ds4.UpdateInput(MapToDualShock4Input(this));
                xbox.UpdateInput(MapToXbox360Input(this));
            }
            catch { } // ignore
            finally
            {
                if (lockTaken)
                {
                    Monitor.Exit(_updateInputLock);
                }

                if (otherLockTaken)
                {
                    Monitor.Exit(Other._updateInputLock);
                }
            }
        }

        // Run from poll thread
        private ReceiveError ReceiveRaw(Span<byte> buf)
        {
            if (_handle == IntPtr.Zero)
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
            if (packetType != (byte)ReportMode.StandardFull && packetType != (byte)ReportMode.SimpleHID)
            {
                return ReceiveError.InvalidPacket;
            }

            // clear remaining of buffer just to be safe
            if (length < ReportLength)
            {
                buf.Slice(length,  ReportLength - length).Clear();
            }

            const int nbPackets = 3;
            ulong deltaPacketsMicroseconds = 0;

            if (packetType == (byte)ReportMode.StandardFull)
            {
                // Determine the IMU timestamp with a rolling average instead of relying on the unreliable packet's timestamp
                // more detailed explanations on why : https://github.com/torvalds/linux/blob/52b1853b080a082ec3749c3a9577f6c71b1d4a90/drivers/hid/hid-nintendo.c#L1115
                if (_timeSinceReceive.IsRunning)
                {
                    var deltaReceiveMs = _timeSinceReceive.ElapsedMilliseconds;
                    _avgReceiveDeltaMs.AddValue((int)deltaReceiveMs);
                }
                _timeSinceReceive.Restart();

                var deltaPacketsMs = _avgReceiveDeltaMs.GetAverage() / nbPackets;
                deltaPacketsMicroseconds = (ulong)(deltaPacketsMs * 1000);

                 _AHRS.SamplePeriod = deltaPacketsMs / 1000;
            }

            // Process packets as soon as they come
            for (var n = 0; n < nbPackets; n++)
            {
                bool updateIMU = ExtractIMUValues(buf, n);

                if (n == 0)
                {
                    ProcessButtonsAndStick(buf);
                    DoThingsWithButtons();
                    GetBatteryInfos(buf);
                }

                if (!updateIMU)
                {
                    break;
                }

                Timestamp += deltaPacketsMicroseconds;
                PacketCounter++;

                if (IsPrimaryGyro)
                {
                    Program.Server?.NewReportIncoming(this);
                }
            }

            UpdateInput();

            //DebugPrint($"Bytes read: {length:D}. Elapsed: {deltaReceiveMs}ms AVG: {_avgReceiveDeltaMs.GetAverage()}ms", DebugType.Threading);

            return ReceiveError.None;
        }

        private void DetectShake()
        {
            if (Config.ShakeInputEnabled && IsPrimaryGyro)
            {
                var currentShakeTime = _shakeTimer.ElapsedMilliseconds;

                // Shake detection logic
                var isShaking = GetAccel().LengthSquared() >= Config.ShakeSensitivity;
                if (isShaking && (currentShakeTime >= _shakedTime + Config.ShakeDelay || _shakedTime == 0))
                {
                    _shakedTime = currentShakeTime;
                    _hasShaked = true;

                    // Mapped shake key down
                    Simulate(Settings.Value("shake"), false);
                    DebugPrint("Shaked at time: " + _shakedTime, DebugType.Shake);
                }

                // If controller was shaked then release mapped key after a small delay to simulate a button press, then reset hasShaked
                if (_hasShaked && currentShakeTime >= _shakedTime + 10)
                {
                    // Mapped shake key up
                    Simulate(Settings.Value("shake"), false, true);
                    DebugPrint("Shake completed", DebugType.Shake);
                    _hasShaked = false;
                }
            }
            else
            {
                _shakeTimer.Stop();
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
                            bool release;
                            _mouseToggleBtn.TryGetValue((int)button, out release);
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

                SimulateContinous((int)Button.SL, Settings.Value("sl_l"));
                SimulateContinous((int)Button.SR, Settings.Value("sr_l"));
            }
            else
            {
                if (_buttonsDown[(int)Button.SL])
                {
                    Simulate(Settings.Value("sl_r"), false);
                }

                if (_buttonsUp[(int)Button.SL])
                {
                    Simulate(Settings.Value("sl_r"), false, true);
                }

                if (_buttonsDown[(int)Button.SR])
                {
                    Simulate(Settings.Value("sr_r"), false);
                }

                if (_buttonsUp[(int)Button.SR])
                {
                    Simulate(Settings.Value("sr_r"), false, true);
                }

                SimulateContinous((int)Button.SL, Settings.Value("sl_r"));
                SimulateContinous((int)Button.SR, Settings.Value("sr_r"));
            }

            SimulateContinous(_hasShaked, Settings.Value("shake"));
        }

        private void RemapButtons()
        {
            lock (_buttonsRemapped)
            {
                lock (_buttons)
                {
                    Array.Copy(_buttons, _buttonsRemapped, _buttons.Length);

                    ReleaseRemappedButtons();
                    SimulateRemappedButtons();
                }
            }
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

        private void DoThingsWithButtons()
        {
            var powerOffButton = (int)(IsPro || !IsLeft || IsJoined ? Button.Home : Button.Capture);
            var timestampNow = Stopwatch.GetTimestamp();

            if (!IsUSB)
            {
                bool powerOff = false;

                if (Config.HomeLongPowerOff && _buttons[powerOffButton])
                {
                    var powerOffPressedDurationMs = (timestampNow - _buttonsDownTimestamp[powerOffButton]) / 10000;
                    if (powerOffPressedDurationMs > 2000)
                    {
                        powerOff = true;
                    }
                }

                if (Config.PowerOffInactivityMins > 0)
                {
                    var timeSinceActivityMs = (timestampNow - _timestampActivity) / 10000;
                    if (timeSinceActivityMs > Config.PowerOffInactivityMins * 60 * 1000)
                    {
                        powerOff = true;
                    }
                }

                if (powerOff)
                {
                    if (IsJoined)
                    {
                        Program.Mgr.PowerOff(Other);
                    }
                    PowerOff();
                    return;
                }
            }

            if (IsJoycon && !_calibrateSticks && !_calibrateIMU)
            {
                if (Config.ChangeOrientationDoubleClick && _buttonsDown[(int)Button.Stick] && _lastDoubleClick != -1)
                {
                    if (_buttonsDownTimestamp[(int)Button.Stick] - _lastDoubleClick < 3000000)
                    {
                        Program.Mgr.JoinOrSplitJoycon(this);

                        _lastDoubleClick = _buttonsDownTimestamp[(int)Button.Stick];
                        return;
                    }

                    _lastDoubleClick = _buttonsDownTimestamp[(int)Button.Stick];
                }
                else if (Config.ChangeOrientationDoubleClick && _buttonsDown[(int)Button.Stick])
                {
                    _lastDoubleClick = _buttonsDownTimestamp[(int)Button.Stick];
                }
            }

            DetectShake();
            RemapButtons();

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

            // Filtered IMU data
            _AHRS.GetEulerAngles(_curRotation);
            float dt = _avgReceiveDeltaMs.GetAverage() / 1000;

            if (Config.GyroAnalogSliders && (Other != null || IsPro))
            {
                var leftT = IsLeft ? Button.Shoulder2 : Button.Shoulder22;
                var rightT = IsLeft ? Button.Shoulder22 : Button.Shoulder2;
                var left = IsLeft || IsPro ? this : Other;
                var right = !IsLeft || IsPro ? this : Other;

                int ldy, rdy;
                if (Config.UseFilteredIMU)
                {
                    ldy = (int)(Config.GyroAnalogSensitivity * (left._curRotation[0] - left._curRotation[3]));
                    rdy = (int)(Config.GyroAnalogSensitivity * (right._curRotation[0] - right._curRotation[3]));
                }
                else
                {
                    ldy = (int)(Config.GyroAnalogSensitivity * (left._gyrG.Y * dt));
                    rdy = (int)(Config.GyroAnalogSensitivity * (right._gyrG.Y * dt));
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

            if (IsPrimaryGyro)
            {
                if (Config.ExtraGyroFeature.StartsWith("joy"))
                {
                    if (Settings.Value("active_gyro") == "0" || ActiveGyro)
                    {
                        var controlStick = Config.ExtraGyroFeature == "joy_left" ? _stick : _stick2;

                        float dx, dy;
                        if (Config.UseFilteredIMU)
                        {
                            dx = Config.GyroStickSensitivityX * (_curRotation[1] - _curRotation[4]); // yaw
                            dy = -(Config.GyroStickSensitivityY * (_curRotation[0] - _curRotation[3])); // pitch
                        }
                        else
                        {
                            dx = Config.GyroStickSensitivityX * (_gyrG.Z * dt); // yaw
                            dy = -(Config.GyroStickSensitivityY * (_gyrG.Y * dt)); // pitch
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
                            dx = (int)(Config.GyroMouseSensitivityX * (_curRotation[1] - _curRotation[4])); // yaw
                            dy = (int)-(Config.GyroMouseSensitivityY * (_curRotation[0] - _curRotation[3])); // pitch
                        }
                        else
                        {
                            dx = (int)(Config.GyroMouseSensitivityX * (_gyrG.Z * dt));
                            dy = (int)-(Config.GyroMouseSensitivityY * (_gyrG.Y * dt));
                        }

                        WindowsInput.Simulate.Events().MoveBy(dx, dy).Invoke();
                    }

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
        }

        private void GetBatteryInfos(ReadOnlySpan<byte> reportBuf)
        {
            byte packetType = reportBuf[0];
            if (packetType != (byte)ReportMode.StandardFull)
            {
                return;
            }

            var prevBattery = Battery;
            var prevCharging = Charging;

            byte highNibble = (byte)(reportBuf[2] >> 4);
            Battery = (BatteryLevel)(Math.Clamp(highNibble >> 1, (byte)BatteryLevel.Empty, (byte)BatteryLevel.Full));
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

        private void SendCommands()
        {
            Span<byte> buf = stackalloc byte[_CommandLength];
            buf.Clear();

            // the home light stays on for 2625ms, set to less than half in case of packet drop
            const int sendHomeLightIntervalMs = 1250;
            Stopwatch timeSinceHomeLight = new();

            while (!_stopPolling && State > Status.Dropped)
            {
                if (Program.IsSuspended)
                {
                    Thread.Sleep(10);
                    continue;
                }

                if (Config.HomeLEDOn && (timeSinceHomeLight.ElapsedMilliseconds > sendHomeLightIntervalMs || !timeSinceHomeLight.IsRunning))
                {
                    SetHomeLight(Config.HomeLEDOn);
                    timeSinceHomeLight.Restart();
                }

                byte[] data;
                while ((data = _rumbleObj.GetData()) != null)
                {
                    SendRumble(buf, data);
                }

                Thread.Sleep(5);
            }
        }

        private void ReceiveReports()
        {
            Span<byte> buf = stackalloc byte[ReportLength];
            buf.Clear();

            int dropAfterMs = IsUSB ? 1500 : 3000;
            Stopwatch timeSinceError = new();
            int reconnectAttempts = 0;

            // For IMU timestamp calculation
            _avgReceiveDeltaMs.Clear();
            _avgReceiveDeltaMs.AddValue(15); // default value of 15ms between packets
            _timeSinceReceive.Reset();
            Timestamp = 0;

            while (!_stopPolling && IsDeviceReady)
            {
                if (Program.IsSuspended)
                {
                    Thread.Sleep(10);
                    continue;
                }

                var error = ReceiveRaw(buf);

                if (error == ReceiveError.None && IsDeviceReady)
                {
                    State = Status.IMUDataOk;
                    timeSinceError.Reset();
                    reconnectAttempts = 0;
                }
                else if (timeSinceError.ElapsedMilliseconds > dropAfterMs)
                {
                    if (IsUSB)
                    {
                        if (reconnectAttempts >= 3)
                        {
                            Log("Dropped.");
                            State = Status.Errored;
                        }
                        else
                        {
                            Log("Attempt soft reconnect...");
                            try
                            {
                                USBPairing();
                                SetReportMode(ReportMode.StandardFull);
                                SetLEDByPadID();
                            }
                            catch { } // ignore and retry
                        }
                    }
                    else
                    {
                        //Log("Attempt soft reconnect...");
                        SetReportMode(ReportMode.StandardFull, false);
                    }

                    timeSinceError.Restart();
                    ++reconnectAttempts;
                }
                else if (error == ReceiveError.InvalidHandle)
                {
                    // should not happen
                    State = Status.Errored;
                    Log("Dropped (invalid handle).");
                }
                else
                {
                    timeSinceError.Start();

                    // No data read, read error or invalid packet
                    if (error == ReceiveError.ReadError)
                    {
                        Thread.Sleep(5); // to avoid spin
                    }
                }
            }
        }

        private static ushort Scale16bitsTo12bits(int value)
        {
            const float scale16bitsTo12bits = 4095f / 65535f;

            return (ushort)MathF.Round(value * scale16bitsTo12bits);
        }

        private void ExtractSticksValues(ReadOnlySpan<byte> reportBuf)
        {
            byte reportType = reportBuf[0];

            if (reportType == (byte)ReportMode.StandardFull)
            {
                var offset = IsLeft ? 0 : 3;

                _stickPrecal[0] = (ushort)(reportBuf[6 + offset] | ((reportBuf[7 + offset] & 0xF) << 8));
                _stickPrecal[1] = (ushort)((reportBuf[7 + offset] >> 4) | (reportBuf[8 + offset] << 4));

                if (IsPro)
                {
                    _stick2Precal[0] = (ushort)(reportBuf[9] | ((reportBuf[10] & 0xF) << 8));
                    _stick2Precal[1] = (ushort)((reportBuf[10] >> 4) | (reportBuf[11] << 4));
                }
            }
            else if (reportType == (byte)ReportMode.SimpleHID)
            {
                if (IsPro)
                {
                    // Scale down to 12 bits to match the calibrations datas precision
                    // Invert y axis by substracting from 0xFFFF to match 0x30 reports 
                    _stickPrecal[0] = Scale16bitsTo12bits(reportBuf[4] | (reportBuf[5] << 8));
                    _stickPrecal[1] = Scale16bitsTo12bits(0XFFFF - (reportBuf[6] | (reportBuf[7] << 8)));

                    _stick2Precal[0] = Scale16bitsTo12bits(reportBuf[8] | (reportBuf[9] << 8));
                    _stick2Precal[1] = Scale16bitsTo12bits(0xFFFF - (reportBuf[10] | (reportBuf[11] << 8)));
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

            if (reportType == (byte)ReportMode.StandardFull)
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

                if (IsPro)
                {
                    _buttons[(int)Button.B] = (reportBuf[3] & 0x04) != 0;
                    _buttons[(int)Button.A] = (reportBuf[3] & 0x08) != 0;
                    _buttons[(int)Button.X] = (reportBuf[3] & 0x02) != 0;
                    _buttons[(int)Button.Y] = (reportBuf[3] & 0x01) != 0;

                    _buttons[(int)Button.Stick2] = (reportBuf[4] & 0x04) != 0;
                    _buttons[(int)Button.Shoulder21] = (reportBuf[3] & 0x40) != 0;
                    _buttons[(int)Button.Shoulder22] = (reportBuf[3] & 0x80) != 0;
                }
            }
            else if (reportType == (byte)ReportMode.SimpleHID)
            {
                _buttons[(int)Button.Home] = (reportBuf[2] & 0x10) != 0;
                _buttons[(int)Button.Capture] = (reportBuf[2] & 0x20) != 0;
                _buttons[(int)Button.Minus] = (reportBuf[2] & 0x01) != 0;
                _buttons[(int)Button.Plus] = (reportBuf[2] & 0x02) != 0;
                _buttons[(int)Button.Stick] = (reportBuf[2] & (IsLeft ? 0x04 : 0x08)) != 0;
                
                if (IsPro)
                {
                    byte stickHat = reportBuf[3];

                    _buttons[(int)Button.DpadDown] = stickHat == 0x03 || stickHat == 0x04 || stickHat == 0x05;
                    _buttons[(int)Button.DpadRight] = stickHat == 0x01 || stickHat == 0x02 || stickHat == 0x03;
                    _buttons[(int)Button.DpadUp] = stickHat == 0x07 || stickHat == 0x00 || stickHat == 0x01;
                    _buttons[(int)Button.DpadLeft] =  stickHat == 0x05 ||  stickHat == 0x06 || stickHat == 0x07;

                    _buttons[(int)Button.B] = (reportBuf[1] & 0x01) != 0;
                    _buttons[(int)Button.A] = (reportBuf[1] & 0x02) != 0;
                    _buttons[(int)Button.X] = (reportBuf[1] & 0x08) != 0;
                    _buttons[(int)Button.Y] = (reportBuf[1] & 0x04) != 0;

                    _buttons[(int)Button.Stick2] = (reportBuf[2] & 0x08) != 0;
                    _buttons[(int)Button.Shoulder1] = (reportBuf[1] & 0x10) != 0;
                    _buttons[(int)Button.Shoulder2] = (reportBuf[1] & 0x40) != 0;
                    _buttons[(int)Button.Shoulder21] = (reportBuf[1] & 0x20) != 0;
                    _buttons[(int)Button.Shoulder22] = (reportBuf[1] & 0x80) != 0;
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

        private void ProcessButtonsAndStick(ReadOnlySpan<byte> reportBuf)
        {
            var activity = false;
            var timestamp = Stopwatch.GetTimestamp();

            if (!IsSNES)
            {
                ExtractSticksValues(reportBuf);

                var cal = _stickCal;
                var dz = _deadzone;
                var range = _range;

                if (_SticksCalibrated)
                {
                    cal = _activeStick1;
                    dz = _activeStick1Deadzone;
                    range = _activeStick1Range;
                }

                CalculateStickCenter(_stickPrecal, cal, dz, range, _stick);

                if (IsPro)
                {
                    cal = _stick2Cal;
                    dz = _deadzone2;
                    range = _range2;

                    if (_SticksCalibrated)
                    {
                        cal = _activeStick2;
                        dz = _activeStick2Deadzone;
                        range = _activeStick2Range;
                    }

                    CalculateStickCenter(_stick2Precal, cal, dz, range, _stick2);
                }
                // Read other Joycon's sticks
                else if (IsJoined)
                {
                    lock (_otherStick)
                    {
                        // Read other stick sent by other joycon
                        if (IsLeft)
                        {
                            Array.Copy(_otherStick, _stick2, 2);
                        }
                        else
                        {
                            _stick = Interlocked.Exchange(ref _stick2, _stick);
                            Array.Copy(_otherStick, _stick, 2);
                        }
                    }

                    lock (Other._otherStick)
                    {
                        // Write stick to linked joycon
                        Array.Copy(IsLeft ? _stick : _stick2, Other._otherStick, 2);
                    }
                }
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

                const float stickActivityThreshold = 0.1f;
                if (MathF.Abs(_stick[0]) > stickActivityThreshold ||
                    MathF.Abs(_stick[1]) > stickActivityThreshold ||
                    MathF.Abs(_stick2[0]) > stickActivityThreshold ||
                    MathF.Abs(_stick2[1]) > stickActivityThreshold)
                {
                    activity = true;
                }
            }

            // Set button states both for ViGEm
            lock (_buttons)
            {
                lock (_buttonsPrev)
                {
                    Array.Copy(_buttons, _buttonsPrev, _buttons.Length);
                }

                Array.Clear(_buttons);

                ExtractButtonsValues(reportBuf);

                if (IsJoined)
                {
                    _buttons[(int)Button.B] = Other._buttons[(int)Button.DpadDown];
                    _buttons[(int)Button.A] = Other._buttons[(int)Button.DpadRight];
                    _buttons[(int)Button.X] = Other._buttons[(int)Button.DpadUp];
                    _buttons[(int)Button.Y] = Other._buttons[(int)Button.DpadLeft];

                    _buttons[(int)Button.Stick2] = Other._buttons[(int)Button.Stick];
                    _buttons[(int)Button.Shoulder21] = Other._buttons[(int)Button.Shoulder1];
                    _buttons[(int)Button.Shoulder22] = Other._buttons[(int)Button.Shoulder2];

                    if (IsLeft)
                    {
                        _buttons[(int)Button.Home] = Other._buttons[(int)Button.Home];
                        _buttons[(int)Button.Plus] = Other._buttons[(int)Button.Plus];
                    }
                    else
                    {
                        _buttons[(int)Button.Capture] = Other._buttons[(int)Button.Capture];
                        _buttons[(int)Button.Minus] = Other._buttons[(int)Button.Minus];
                    }
                }

                lock (_buttonsUp)
                {
                    lock (_buttonsDown)
                    {
                        for (var i = 0; i < _buttons.Length; ++i)
                        {
                            _buttonsUp[i] = _buttonsPrev[i] & !_buttons[i];
                            _buttonsDown[i] = !_buttonsPrev[i] & _buttons[i];
                            if (_buttonsPrev[i] != _buttons[i])
                            {
                                _buttonsDownTimestamp[i] = _buttons[i] ? timestamp : -1;
                            }

                            if (_buttonsUp[i] || _buttonsDown[i])
                            {
                                activity = true;
                            }
                        }
                    }
                }
            }

            if (activity)
            {
                _timestampActivity = timestamp;
            }
        }

        // Get Gyro/Accel data
        private bool ExtractIMUValues(ReadOnlySpan<byte> reportBuf, int n = 0)
        {
            if (IsSNES || reportBuf[0] != (byte)ReportMode.StandardFull)
            {
                return false;
            }

            _gyrRaw[0] = (short)(reportBuf[19 + n * 12] | (reportBuf[20 + n * 12] << 8));
            _gyrRaw[1] = (short)(reportBuf[21 + n * 12] | (reportBuf[22 + n * 12] << 8));
            _gyrRaw[2] = (short)(reportBuf[23 + n * 12] | (reportBuf[24 + n * 12] << 8));
            _accRaw[0] = (short)(reportBuf[13 + n * 12] | (reportBuf[14 + n * 12] << 8));
            _accRaw[1] = (short)(reportBuf[15 + n * 12] | (reportBuf[16 + n * 12] << 8));
            _accRaw[2] = (short)(reportBuf[17 + n * 12] | (reportBuf[18 + n * 12] << 8));

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

                _accG.Y = direction * (_accRaw[1] -_activeIMUData[4]) * (1.0f / (_accSensiti[1] - _accNeutral[1])) * 4.0f;
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
            if (_receiveReportsThread == null && _sendCommandsThread == null)
            {
                _receiveReportsThread = new Thread(ReceiveReports)
                {
                    IsBackground = true
                };

                _sendCommandsThread = new Thread(SendCommands)
                {
                    IsBackground = true
                };

                _stopPolling = false;
                _sendCommandsThread.Start();
                _receiveReportsThread.Start();

                Log("Ready.");
            }
            else
            {
                Log("Poll thread cannot start!");
            }
        }

        private void CalculateStickCenter(ushort[] vals, ushort[] cal, float deadzone, float range, float[] stick)
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
                float normalizedMagnitude = Math.Min(1.0f, (magnitude - deadzone) / (range - deadzone));
                float scale = normalizedMagnitude / magnitude;
                
                normalizedX *= scale;
                normalizedY *= scale;

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
                        stick[0] = Math.Sign(normalizedX) * normalizedMagnitude;
                        stick[1] = stick[0] * normalizedY / normalizedX;
                    }
                    else
                    {
                        stick[1] = Math.Sign(normalizedY) * normalizedMagnitude;
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

        public void SetRumble(float lowFreq, float highFreq, float amp)
        {
            if (State <= Status.Attached)
            {
                return;
            }

            _rumbleObj.Enqueue(lowFreq, highFreq, amp);
        }

        // Run from poll thread
        private void SendRumble(Span<byte> buf, ReadOnlySpan<byte> data)
        {
            buf.Clear();

            buf[0] = 0x10;
            buf[1] = (byte)(_globalCount & 0x0F);
            ++_globalCount;

            data.Slice(0, 8).CopyTo(buf.Slice(2));
            PrintArray<byte>(buf, DebugType.Rumble, 10, format: "Rumble data sent: {0:S}");
            Write(buf);
        }

        private bool Subcommand(byte sc, ReadOnlySpan<byte> bufParameters, bool print = true)
        {
            if (_handle == IntPtr.Zero)
            {
                return false;
            }

            Span<byte> buf = stackalloc byte[_CommandLength];
            buf.Clear();

            _defaultBuf.AsSpan(0, 8).CopyTo(buf.Slice(2));
            bufParameters.CopyTo(buf.Slice(11));
            buf[10] = sc;
            buf[1] = (byte)(_globalCount & 0x0F);
            buf[0] = 0x01;
            ++_globalCount;

            if (print)
            {
                PrintArray<byte>(buf, DebugType.Comms, bufParameters.Length, 11, $"Subcommand {sc:X2} sent." + " Data: {0:S}");
            }

            int length = Write(buf);

            return length > 0;
        }

        private int SubcommandCheck(byte sc, ReadOnlySpan<byte> bufParameters, bool print = true)
        {
            Span<byte> response = stackalloc byte[ReportLength];

            return SubcommandCheck(sc, bufParameters, response, print);
        }

        private int SubcommandCheck(byte sc, ReadOnlySpan<byte> bufParameters, Span<byte> response, bool print = true)
        {
            bool sent = Subcommand(sc, bufParameters, print);
            if (!sent)
            {
                DebugPrint($"Subcommand write error: {ErrorMessage()}", DebugType.Comms);
                return 0;
            }

            int tries = 0;
            int length;
            bool responseFound;
            do
            {
                length = Read(response, 100); // don't set the timeout lower than 100 or might not always work
                responseFound = length >= 20 && response[0] == 0x21 && response[14] == sc;
                
                if (length < 0)
                {
                    DebugPrint($"Subcommand read error: {ErrorMessage()}", DebugType.Comms);
                }

                tries++;
            } while (tries < 10 && !responseFound && length >= 0);

            if (!responseFound)
            {
                DebugPrint("No response.", DebugType.Comms);
                return 0;
            }

            if (print)
            {
                PrintArray<byte>(
                    response,
                    DebugType.Comms,
                    length - 1,
                    1,
                    $"Response ID {response[0]:X2}." + " Data: {0:S}"
                );
            }

            return length;
        }

        private float CalculateDeadzone(ushort[] cal, ushort deadzone)
        {
            return 2.0f * deadzone / Math.Max(cal[0] + cal[4], cal[1] + cal[5]);
        }

        private float CalculateRange(ushort range)
        {
            return (float)range / 0xFFF;
        }

        private bool CalibrationDataSupported()
        {
            return !IsSNES && !IsThirdParty;
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

                _deadzone = Config.DefaultDeadzone;
                _deadzone2 = Config.DefaultDeadzone;

                _range = Config.DefaultRange;
                _range2 = Config.DefaultRange;

                _DumpedCalibration = false;

                return true;
            }

            var ok = true;

            // get user calibration data if possible

            // Sticks axis
            {
                var userStickData = ReadSPICheck(0x80, 0x10, 0x16, ref ok);
                var factoryStickData = ReadSPICheck(0x60, 0x3D, 0x12, ref ok);

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

                _stickCal[IsLeft ? 0 : 2] = (ushort)(((stick1Data[1] << 8) & 0xF00) | stick1Data[0]); // X Axis Max above center
                _stickCal[IsLeft ? 1 : 3] = (ushort)((stick1Data[2] << 4) | (stick1Data[1] >> 4)); // Y Axis Max above center
                _stickCal[IsLeft ? 2 : 4] = (ushort)(((stick1Data[4] << 8) & 0xF00) | stick1Data[3]); // X Axis Center
                _stickCal[IsLeft ? 3 : 5] = (ushort)((stick1Data[5] << 4) | (stick1Data[4] >> 4)); // Y Axis Center
                _stickCal[IsLeft ? 4 : 0] = (ushort)(((stick1Data[7] << 8) & 0xF00) | stick1Data[6]); // X Axis Min below center
                _stickCal[IsLeft ? 5 : 1] = (ushort)((stick1Data[8] << 4) | (stick1Data[7] >> 4)); // Y Axis Min below center

                PrintArray<ushort>(_stickCal, len: 6, start: 0, format: $"{stick1Name} stick 1 calibration data: {{0:S}}");

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

                    _stick2Cal[!IsLeft ? 0 : 2] = (ushort)(((stick2Data[1] << 8) & 0xF00) | stick2Data[0]); // X Axis Max above center
                    _stick2Cal[!IsLeft ? 1 : 3] = (ushort)((stick2Data[2] << 4) | (stick2Data[1] >> 4)); // Y Axis Max above center
                    _stick2Cal[!IsLeft ? 2 : 4] = (ushort)(((stick2Data[4] << 8) & 0xF00) | stick2Data[3]); // X Axis Center
                    _stick2Cal[!IsLeft ? 3 : 5] = (ushort)((stick2Data[5] << 4) | (stick2Data[4] >> 4)); // Y Axis Center
                    _stick2Cal[!IsLeft ? 4 : 0] = (ushort)(((stick2Data[7] << 8) & 0xF00) | stick2Data[6]); // X Axis Min below center
                    _stick2Cal[!IsLeft ? 5 : 1] = (ushort)((stick2Data[8] << 4) | (stick2Data[7] >> 4)); // Y Axis Min below center

                    PrintArray<ushort>(_stick2Cal, len: 6, start: 0, format: $"{stick2Name} stick calibration data: {{0:S}}");
                }
            }

            // Sticks deadzones and ranges
            // Looks like the range is a 12 bits precision ratio.
            // I suppose the right way to interpret it is as a float by dividing it by 0xFFF
            {
                var factoryDeadzoneData = ReadSPICheck(0x60, IsLeft ? (byte)0x86 : (byte)0x98, 6, ref ok);

                var deadzone = (ushort)(((factoryDeadzoneData[4] << 8) & 0xF00) | factoryDeadzoneData[3]);
                _deadzone = CalculateDeadzone(_stickCal, deadzone);

                var range = (ushort)((factoryDeadzoneData[5] << 4) | (factoryDeadzoneData[4] >> 4));
                _range = CalculateRange(range);

                if (IsPro)
                {
                    var factoryDeadzone2Data = ReadSPICheck(0x60, !IsLeft ? (byte)0x86 : (byte)0x98, 6, ref ok);

                    var deadzone2 = (ushort)(((factoryDeadzone2Data[4] << 8) & 0xF00) | factoryDeadzone2Data[3]);
                    _deadzone2 = CalculateDeadzone(_stick2Cal, deadzone2);

                    var range2 = (ushort)((factoryDeadzone2Data[5] << 4) | (factoryDeadzone2Data[4] >> 4));
                    _range2 = CalculateRange(range2);
                }
            }

            // Gyro and accelerometer
            {
                var userSensorData = ReadSPICheck(0x80, 0x26, 0x1A, ref ok);
                ReadOnlySpan<byte> sensorData = new ReadOnlySpan<byte>(userSensorData, 2, 24);

                if (ok)
                {
                    if (userSensorData[0] == 0xB2 && userSensorData[1] == 0xA1)
                    {
                        DebugPrint($"Retrieve user sensors calibration data.", DebugType.Comms);
                    }
                    else
                    {
                        var factorySensorData = ReadSPICheck(0x60, 0x20, 0x18, ref ok);
                        sensorData = new ReadOnlySpan<byte>(factorySensorData, 0, 24);

                        DebugPrint($"Retrieve factory sensors calibration data.", DebugType.Comms);
                    }
                }

                _accNeutral[0] = (short)(sensorData[0] | (sensorData[1] << 8));
                _accNeutral[1] = (short)(sensorData[2] | (sensorData[3] << 8));
                _accNeutral[2] = (short)(sensorData[4] | (sensorData[5] << 8));

                _accSensiti[0] = (short)(sensorData[6] | (sensorData[7] << 8));
                _accSensiti[1] = (short)(sensorData[8] | (sensorData[9] << 8));
                _accSensiti[2] = (short)(sensorData[10] | (sensorData[11] << 8));

                _gyrNeutral[0] = (short)(sensorData[12] | (sensorData[13] << 8));
                _gyrNeutral[1] = (short)(sensorData[14] | (sensorData[15] << 8));
                _gyrNeutral[2] = (short)(sensorData[16] | (sensorData[17] << 8));

                _gyrSensiti[0] = (short)(sensorData[18] | (sensorData[19] << 8));
                _gyrSensiti[1] = (short)(sensorData[20] | (sensorData[21] << 8));
                _gyrSensiti[2] = (short)(sensorData[22] | (sensorData[23] << 8));

                bool noCalibration = false;

                if (_accNeutral[0] == -1 || _accNeutral[1] == -1 || _accNeutral[2] == -1)
                {
                    Array.Fill(_accNeutral, (short) 0);
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
                    Log($"Some sensor calibrations datas are missing, fallback to default ones.");
                }

                PrintArray<short>(_gyrNeutral, len: 3, d: DebugType.IMU, format: "Gyro neutral position: {0:S}");
            }

            if (!ok)
            {
                Log("Error while reading calibration datas.");
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

            calibrationType = _IMUCalibrated ? "user" : _DumpedCalibration ? "controller" : "default";
            Log($"Using {calibrationType} sensors calibration.");
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
                return HIDApi.ReadTimeout(_handle, response, ReportLength, timeout);
            }
            return HIDApi.Read(_handle, response, ReportLength);
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

            int length = HIDApi.Write(_handle, command, _CommandLength);
            return length;
        }

        private string ErrorMessage()
        {
            if (IsDeviceError)
            {
                return $"Device unavailable : {State}";
            }
            return HIDApi.Error(_handle);
        }

        private bool USBCommand(byte command, bool print = true)
        {
            if (_handle == IntPtr.Zero)
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
                    response,
                    DebugType.Comms,
                    length - 1,
                    1,
                    $"USB response ID {response[0]:X2}." + " Data: {0:S}"
                );
            }

            return length;
        }

        private byte[] ReadSPICheck(byte addr1, byte addr2, int len, ref bool ok, bool print = false)
        {
            var readBuf = new byte[len];
            if (!ok)
            {
                return readBuf;
            }

            byte[] bufSubcommand = { addr2, addr1, 0x00, 0x00, (byte)len };
            
            Span<byte> response = stackalloc byte[ReportLength];

            ok = false;
            for (var i = 0; i < 5; ++i)
            {
                int length = SubcommandCheck(0x10, bufSubcommand, response, false);
                if (length >= 20 + len && response[15] == addr2 && response[16] == addr1)
                {
                    ok = true;
                    break;
                }
            }

            if (ok)
            {
                response.Slice(20, len).CopyTo(readBuf);
                if (print)
                {
                    PrintArray<byte>(readBuf, DebugType.Comms, len);
                }
            }
            else
            {
                Log("ReadSPI error");
            }

            return readBuf;
        }

        private void PrintArray<T>(
            ReadOnlySpan<T> arr,
            DebugType d = DebugType.None,
            int len = 0,
            int start = 0,
            string format = "{0:S}"
        )
        {
            if (d != Config.DebugType && Config.DebugType != DebugType.All)
            {
                return;
            }

            if (len == 0)
            {
                len = arr.Length;
            }

            var tostr = "";
            for (var i = 0; i < len; ++i)
            {
                tostr += string.Format(
                    arr[0] is byte ? "{0:X2} " : arr[0] is float ? "{0:F} " : "{0:D} ",
                    arr[i + start]
                );
            }

            DebugPrint(string.Format(format, tostr), d);
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

            var isPro = input.IsPro;
            var isLeft = input.IsLeft;
            var isSNES = input.IsSNES;
            var other = input.Other;

            var buttons = input._buttonsRemapped;
            var stick = input._stick;
            var stick2 = input._stick2;
            var sliderVal = input._sliderVal;

            var gyroAnalogSliders = input.Config.GyroAnalogSliders;
            var swapAB = input.Config.SwapAB;
            var swapXY = input.Config.SwapXY;

            if (other != null && !isLeft)
            {
                gyroAnalogSliders = other.Config.GyroAnalogSliders;
                swapAB = other.Config.SwapAB;
                swapXY = other.Config.SwapXY;
            }

            if (isPro)
            {
                output.A = buttons[(int)(!swapAB ? Button.B : Button.A)];
                output.B = buttons[(int)(!swapAB ? Button.A : Button.B)];
                output.Y = buttons[(int)(!swapXY ? Button.X : Button.Y)];
                output.X = buttons[(int)(!swapXY ? Button.Y : Button.X)];

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
            else
            {
                // no need for && other != this
                if (other != null)
                {
                    output.A = !swapAB
                            ? buttons[(int)(isLeft ? Button.B : Button.DpadDown)]
                            : buttons[(int)(isLeft ? Button.A : Button.DpadRight)];
                    output.B = !swapAB
                            ? buttons[(int)(isLeft ? Button.A : Button.DpadRight)]
                            : buttons[(int)(isLeft ? Button.B : Button.DpadDown)];
                    output.X = !swapXY
                            ? buttons[(int)(isLeft ? Button.Y : Button.DpadLeft)]
                            : buttons[(int)(isLeft ? Button.X : Button.DpadUp)];
                    output.Y = !swapXY
                            ? buttons[(int)(isLeft ? Button.X : Button.DpadUp)]
                            : buttons[(int)(isLeft ? Button.Y : Button.DpadLeft)];

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
                    // single joycon mode
                    output.A = !swapAB
                            ? buttons[(int)(isLeft ? Button.DpadLeft : Button.DpadRight)]
                            : buttons[(int)(isLeft ? Button.DpadDown : Button.DpadUp)];
                    output.B = !swapAB
                            ? buttons[(int)(isLeft ? Button.DpadDown : Button.DpadUp)]
                            : buttons[(int)(isLeft ? Button.DpadLeft : Button.DpadRight)];
                    output.X = !swapXY
                            ? buttons[(int)(isLeft ? Button.DpadUp : Button.DpadDown)]
                            : buttons[(int)(isLeft ? Button.DpadRight : Button.DpadLeft)];
                    output.Y = !swapXY
                            ? buttons[(int)(isLeft ? Button.DpadRight : Button.DpadLeft)]
                            : buttons[(int)(isLeft ? Button.DpadUp : Button.DpadDown)];

                    output.Back = buttons[(int)Button.Minus] | buttons[(int)Button.Home];
                    output.Start = buttons[(int)Button.Plus] | buttons[(int)Button.Capture];

                    output.ShoulderLeft = buttons[(int)Button.SL];
                    output.ShoulderRight = buttons[(int)Button.SR];

                    output.ThumbStickLeft = buttons[(int)Button.Stick];
                }
            }

            if (!isSNES)
            {
                if (other != null || isPro)
                {
                    // no need for && other != this
                    output.AxisLeftX = CastStickValue(other == input && !isLeft ? stick2[0] : stick[0]);
                    output.AxisLeftY = CastStickValue(other == input && !isLeft ? stick2[1] : stick[1]);

                    output.AxisRightX = CastStickValue(other == input && !isLeft ? stick[0] : stick2[0]);
                    output.AxisRightY = CastStickValue(other == input && !isLeft ? stick[1] : stick2[1]);
                }
                else
                {
                    // single joycon mode
                    output.AxisLeftY = CastStickValue((isLeft ? 1 : -1) * stick[0]);
                    output.AxisLeftX = CastStickValue((isLeft ? -1 : 1) * stick[1]);
                }
            }

            if (isPro || other != null)
            {
                var lval = gyroAnalogSliders ? sliderVal[0] : byte.MaxValue;
                var rval = gyroAnalogSliders ? sliderVal[1] : byte.MaxValue;
                output.TriggerLeft = (byte)(buttons[(int)(isLeft ? Button.Shoulder2 : Button.Shoulder22)] ? lval : 0);
                output.TriggerRight = (byte)(buttons[(int)(isLeft ? Button.Shoulder22 : Button.Shoulder2)] ? rval : 0);
            }
            else
            {
                output.TriggerLeft = (byte)(buttons[(int)(isLeft ? Button.Shoulder2 : Button.Shoulder1)] ? byte.MaxValue : 0);
                output.TriggerRight = (byte)(buttons[(int)(isLeft ? Button.Shoulder1 : Button.Shoulder2)] ? byte.MaxValue : 0);
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

            return output;
        }

        public static OutputControllerDualShock4InputState MapToDualShock4Input(Joycon input)
        {
            var output = new OutputControllerDualShock4InputState();

            var isPro = input.IsPro;
            var isLeft = input.IsLeft;
            var isSNES = input.IsSNES;
            var other = input.Other;

            var buttons = input._buttonsRemapped;
            var stick = input._stick;
            var stick2 = input._stick2;
            var sliderVal = input._sliderVal;

            var gyroAnalogSliders = input.Config.GyroAnalogSliders;
            var swapAB = input.Config.SwapAB;
            var swapXY = input.Config.SwapXY;

            if (other != null && !isLeft)
            {
                gyroAnalogSliders = other.Config.GyroAnalogSliders;
                swapAB = other.Config.SwapAB;
                swapXY = other.Config.SwapXY;
            }

            if (isPro)
            {
                output.Cross = buttons[(int)(!swapAB ? Button.B : Button.A)];
                output.Circle = buttons[(int)(!swapAB ? Button.A : Button.B)];
                output.Triangle = buttons[(int)(!swapXY ? Button.X : Button.Y)];
                output.Square = buttons[(int)(!swapXY ? Button.Y : Button.X)];

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
            else
            {
                if (other != null)
                {
                    // no need for && other != this
                    output.Cross = !swapAB
                            ? buttons[(int)(isLeft ? Button.B : Button.DpadDown)]
                            : buttons[(int)(isLeft ? Button.A : Button.DpadRight)];
                    output.Circle = swapAB
                            ? buttons[(int)(isLeft ? Button.B : Button.DpadDown)]
                            : buttons[(int)(isLeft ? Button.A : Button.DpadRight)];
                    output.Triangle = !swapXY
                            ? buttons[(int)(isLeft ? Button.X : Button.DpadUp)]
                            : buttons[(int)(isLeft ? Button.Y : Button.DpadLeft)];
                    output.Square = swapXY
                            ? buttons[(int)(isLeft ? Button.X : Button.DpadUp)]
                            : buttons[(int)(isLeft ? Button.Y : Button.DpadLeft)];

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
                    // single joycon mode
                    output.Cross = !swapAB
                            ? buttons[(int)(isLeft ? Button.DpadLeft : Button.DpadRight)]
                            : buttons[(int)(isLeft ? Button.DpadDown : Button.DpadUp)];
                    output.Circle = swapAB
                            ? buttons[(int)(isLeft ? Button.DpadLeft : Button.DpadRight)]
                            : buttons[(int)(isLeft ? Button.DpadDown : Button.DpadUp)];
                    output.Triangle = !swapXY
                            ? buttons[(int)(isLeft ? Button.DpadRight : Button.DpadLeft)]
                            : buttons[(int)(isLeft ? Button.DpadUp : Button.DpadDown)];
                    output.Square = swapXY
                            ? buttons[(int)(isLeft ? Button.DpadRight : Button.DpadLeft)]
                            : buttons[(int)(isLeft ? Button.DpadUp : Button.DpadDown)];

                    output.Ps = buttons[(int)Button.Minus] | buttons[(int)Button.Home];
                    output.Options = buttons[(int)Button.Plus] | buttons[(int)Button.Capture];

                    output.ShoulderLeft = buttons[(int)Button.SL];
                    output.ShoulderRight = buttons[(int)Button.SR];

                    output.ThumbLeft = buttons[(int)Button.Stick];
                }
            }

            if (!isSNES)
            {
                if (other != null || isPro)
                {
                    // no need for && other != this
                    output.ThumbLeftX = CastStickValueByte(other == input && !isLeft ? stick2[0] : stick[0]);
                    output.ThumbLeftY = CastStickValueByte(other == input && !isLeft ? -stick2[1] : -stick[1]);
                    output.ThumbRightX = CastStickValueByte(other == input && !isLeft ? stick[0] : stick2[0]);
                    output.ThumbRightY = CastStickValueByte(other == input && !isLeft ? -stick[1] : -stick2[1]);

                    //input.DebugPrint($"X:{-stick[0]:0.00} Y:{stick[1]:0.00}", DebugType.Threading);
                    //input.DebugPrint($"X:{output.ThumbLeftX} Y:{output.ThumbLeftY}", DebugType.Threading);
                }
                else
                {
                    // single joycon mode
                    output.ThumbLeftY = CastStickValueByte((isLeft ? 1 : -1) * -stick[0]);
                    output.ThumbLeftX = CastStickValueByte((isLeft ? 1 : -1) * -stick[1]);
                }
            }

            if (isPro || other != null)
            {
                var lval = gyroAnalogSliders ? sliderVal[0] : byte.MaxValue;
                var rval = gyroAnalogSliders ? sliderVal[1] : byte.MaxValue;
                output.TriggerLeftValue = (byte)(buttons[(int)(isLeft ? Button.Shoulder2 : Button.Shoulder22)] ? lval : 0);
                output.TriggerRightValue = (byte)(buttons[(int)(isLeft ? Button.Shoulder22 : Button.Shoulder2)] ? rval : 0);
            }
            else
            {
                output.TriggerLeftValue = (byte)(buttons[(int)(isLeft ? Button.Shoulder2 : Button.Shoulder1)] ? byte.MaxValue : 0);
                output.TriggerRightValue = (byte)(buttons[(int)(isLeft ? Button.Shoulder1 : Button.Shoulder2)] ? byte.MaxValue : 0);
            }

            // Output digital L2 / R2 in addition to analog L2 / R2
            output.TriggerLeft = output.TriggerLeftValue > 0;
            output.TriggerRight = output.TriggerRightValue > 0;

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

        private void Log(string message)
        {
            _form.AppendTextBox($"[P{PadId + 1}] {message}");
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

            if (oldConfig.HomeLEDOn != Config.HomeLEDOn)
            {
                SetHomeLight(Config.HomeLEDOn);
            }

            if (oldConfig.DefaultDeadzone != Config.DefaultDeadzone)
            {
                if (!CalibrationDataSupported())
                {
                    _deadzone = Config.DefaultDeadzone;
                    _deadzone2 = Config.DefaultDeadzone;
                }

                _activeStick1Deadzone = Config.DefaultDeadzone;
                _activeStick2Deadzone = Config.DefaultDeadzone;
            }

            if (oldConfig.DefaultRange != Config.DefaultRange)
            {
                if (!CalibrationDataSupported())
                {
                    _range = Config.DefaultRange;
                    _range2 = Config.DefaultRange;
                }

                _activeStick1Range = Config.DefaultRange;
                _activeStick2Range = Config.DefaultRange;
            }

            if (oldConfig.AllowCalibration != Config.AllowCalibration)
            {
                SetCalibration(Config.AllowCalibration);
            }
        }
        private struct Rumble
        {
            private readonly Queue<float[]> _queue;
            private SpinLock _queueLock;

            public void Enqueue(float lowFreq, float highFreq, float amplitude)
            {
                float[] rumbleQueue = { lowFreq, highFreq, amplitude };
                // Keep a queue of 15 items, discard oldest item if queue is full.
                var lockTaken = false;
                try
                {
                    _queueLock.Enter(ref lockTaken);
                    if (_queue.Count > 15)
                    {
                        _queue.Dequeue();
                    }

                    _queue.Enqueue(rumbleQueue);
                }
                finally
                {
                    if (lockTaken)
                    {
                        _queueLock.Exit();
                    }
                }
            }

            public Rumble(float[] rumbleInfo)
            {
                _queue = new Queue<float[]>();
                _queueLock = new SpinLock();
                _queue.Enqueue(rumbleInfo);
            }

            private byte EncodeAmp(float amp)
            {
                byte enAmp;

                if (amp == 0)
                {
                    enAmp = 0;
                }
                else if (amp < 0.117)
                {
                    enAmp = (byte)((MathF.Log(amp * 1000, 2) * 32 - 0x60) / (5 - MathF.Pow(amp, 2)) - 1);
                }
                else if (amp < 0.23)
                {
                    enAmp = (byte)(MathF.Log(amp * 1000, 2) * 32 - 0x60 - 0x5c);
                }
                else
                {
                    enAmp = (byte)((MathF.Log(amp * 1000, 2) * 32 - 0x60) * 2 - 0xf6);
                }

                return enAmp;
            }

            public byte[] GetData()
            {
                float[] queuedData = null;
                var lockTaken = false;
                try
                {
                    _queueLock.Enter(ref lockTaken);
                    if (_queue.Count > 0)
                    {
                        queuedData = _queue.Dequeue();
                    }
                }
                finally
                {
                    if (lockTaken)
                    {
                        _queueLock.Exit();
                    }
                }

                if (queuedData == null)
                {
                    return null;
                }

                var rumbleData = new byte[8];

                if (queuedData[2] == 0.0f)
                {
                    rumbleData[0] = 0x0;
                    rumbleData[1] = 0x1;
                    rumbleData[2] = 0x40;
                    rumbleData[3] = 0x40;
                }
                else
                {
                    queuedData[0] = Math.Clamp(queuedData[0], 40.875885f, 626.286133f);
                    queuedData[1] = Math.Clamp(queuedData[1], 81.75177f, 1252.572266f);
                    queuedData[2] = Math.Clamp(queuedData[2], 0.0f, 1.0f);

                    var hf = (ushort)((MathF.Round(32f * MathF.Log(queuedData[1] * 0.1f, 2)) - 0x60) * 4);
                    var lf = (byte)(MathF.Round(32f * MathF.Log(queuedData[0] * 0.1f, 2)) - 0x40);

                    var hfAmp = EncodeAmp(queuedData[2]);
                    var lfAmp = (ushort)(MathF.Round(hfAmp) * 0.5f); // weird rounding, is that correct ?

                    var parity = (byte)(lfAmp % 2);
                    if (parity > 0)
                    {
                        --lfAmp;
                    }

                    lfAmp = (ushort)(lfAmp >> 1);
                    lfAmp += 0x40;
                    if (parity > 0)
                    {
                        lfAmp |= 0x8000;
                    }

                    hfAmp = (byte)(hfAmp - hfAmp % 2); // make even at all times to prevent weird hum
                    rumbleData[0] = (byte)(hf & 0xff);
                    rumbleData[1] = (byte)(((hf >> 8) & 0xff) + hfAmp);
                    rumbleData[2] = (byte)(((lfAmp >> 8) & 0xff) + lf);
                    rumbleData[3] = (byte)(lfAmp & 0xff);
                }

                for (var i = 0; i < 4; ++i)
                {
                    rumbleData[4 + i] = rumbleData[i];
                }

                return rumbleData;
            }
        }

        public struct IMUData
        {
            public short Xg;
            public short Yg;
            public short Zg;
            public short Xa;
            public short Ya;
            public short Za;

            public IMUData(short xg, short yg, short zg, short xa, short ya, short za)
            {
                Xg = xg;
                Yg = yg;
                Zg = zg;
                Xa = xa;
                Ya = ya;
                Za = za;
            }
        }

        public struct SticksData
        {
            public ushort Xs1;
            public ushort Ys1;
            public ushort Xs2;
            public ushort Ys2;

            public SticksData(ushort x1, ushort y1, ushort x2, ushort y2)
            {
                Xs1 = x1;
                Ys1 = y1;
                Xs2 = x2;
                Ys2 = y2;
            }
        }

        class RollingAverage
        {
            private Queue<int> _samples;
            private int _size;
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
}
