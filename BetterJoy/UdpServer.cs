using System;
using System.Collections.Generic;
using System.IO.Hashing;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using BetterJoy.Memory;

namespace BetterJoy;

internal class UdpServer
{
    public enum ControllerState : byte
    {
        Disconnected = 0x00,
        Connected = 0x02
    };

    public enum ControllerConnection : byte
    {
        None = 0x00,
        USB = 0x01,
        Bluetooth = 0x02
    };

    public enum ControllerModel : byte
    {
        None = 0x00,
        DS3 = 0x01,
        DS4 = 0x02,
        Generic = 0x03
    }

    public enum ControllerBattery : byte
    {
        Empty = 0x00,
        Critical = 0x01,
        Low = 0x02,
        Medium = 0x03,
        High = 0x04,
        Full = 0x05,
        Charging = 0xEE,
        Charged = 0xEF
    };

    private enum MessageType
    {
        DsucVersionReq = 0x100000,
        DsusVersionRsp = 0x100000,
        DsucListPorts = 0x100001,
        DsusPortInfo = 0x100001,
        DsucPadDataReq = 0x100002,
        DsusPadDataRsp = 0x100002
    }

    private const ushort MaxProtocolVersion = 1001;
    private const int PacketSize = 1024;
    private const int ReportSize = 100;
    private const int ControllerTimeoutSeconds = 5;

    private readonly Dictionary<IPEndPoint, ClientRequestTimes> _clients = new();
    private readonly IList<Joycon> _controllers;

    private volatile bool _running = false;
    private CancellationTokenSource _ctsTransfers;
    private Task _receiveTask;

    private uint _serverId;
    private Socket _udpSock;

    private readonly MainForm _form;

    public UdpServer(MainForm form, IList<Joycon> p)
    {
        _controllers = p;
        _form = form;
    }

    private int BeginPacket(Span<byte> packetBuffer, ushort reqProtocolVersion = MaxProtocolVersion)
    {
        var currIdx = 0;
        packetBuffer[currIdx++] = (byte)'D';
        packetBuffer[currIdx++] = (byte)'S';
        packetBuffer[currIdx++] = (byte)'U';
        packetBuffer[currIdx++] = (byte)'S';

        BitConverter.TryWriteBytes(packetBuffer.Slice(currIdx, 2), reqProtocolVersion);
        currIdx += 2;

        BitConverter.TryWriteBytes(packetBuffer.Slice(currIdx, 2), (ushort)(packetBuffer.Length - 16));
        currIdx += 2;

        packetBuffer.Slice(currIdx, 4).Clear(); //place for crc
        currIdx += 4;

        BitConverter.TryWriteBytes(packetBuffer.Slice(currIdx, 4), _serverId);
        currIdx += 4;

        return currIdx;
    }

    private void FinishPacket(Span<byte> packetBuffer)
    {
        CalculateCrc32(packetBuffer, packetBuffer.Slice(8, 4));
    }

    private async Task SendPacket(
        IPEndPoint clientEp,
        byte[] usefulData,
        ushort reqProtocolVersion = MaxProtocolVersion
    )
    {
        var size = usefulData.Length + 16;
        using var packetDataBuffer = ArrayPoolHelper<byte>.Shared.RentCleared(size);

        // needed to use span in async function
        void MakePacket()
        {
            var packetData = packetDataBuffer.Span;

            var currIdx = BeginPacket(packetData, reqProtocolVersion);
            usefulData.AsSpan().CopyTo(packetData.Slice(currIdx));
            FinishPacket(packetData);
        }

        MakePacket();

        try
        {
            await _udpSock.SendToAsync(clientEp, packetDataBuffer.ReadOnlyMemory);
        }
        catch (SocketException) { }
    }

    private static bool CheckIncomingValidity(Span<byte> localMsg, out int currIdx)
    {
        currIdx = 0;

        if (localMsg.Length < 20)
        {
            return false;
        }

        if (localMsg[0] != 'D' || localMsg[1] != 'S' || localMsg[2] != 'U' || localMsg[3] != 'C')
        {
            return false;
        }

        currIdx += 4;

        uint protocolVer = BitConverter.ToUInt16(localMsg.Slice(currIdx, 2));
        currIdx += 2;

        if (protocolVer > MaxProtocolVersion)
        {
            return false;
        }

        uint packetSize = BitConverter.ToUInt16(localMsg.Slice(currIdx, 2));
        currIdx += 2;

        packetSize += 16; //size of header
        if (packetSize > localMsg.Length)
        {
            return false;
        }

        var crcValue = BitConverter.ToUInt32(localMsg.Slice(currIdx, 4));

        //zero out the crc32 in the packet once we got it since that's whats needed for calculation
        localMsg[currIdx++] = 0;
        localMsg[currIdx++] = 0;
        localMsg[currIdx++] = 0;
        localMsg[currIdx++] = 0;

        var crcCalc = CalculateCrc32(localMsg.Slice(0, (int)packetSize));
        if (crcValue != crcCalc)
        {
            return false;
        }

        return true;
    }

    private List<byte[]> ProcessIncoming(Span<byte> localMsg, IPEndPoint clientEp)
    {
        var replies = new List<byte[]>();

        if (!CheckIncomingValidity(localMsg, out var currIdx))
        {
            return replies;
        }

        // uint clientId = BitConverter.ToUInt32(localMsg.Slice(currIdx, 4));
        currIdx += 4;

        var messageType = BitConverter.ToUInt32(localMsg.Slice(currIdx, 4));
        currIdx += 4;

        switch (messageType)
        {
            case (uint)MessageType.DsucVersionReq:
            {
                var outputData = new byte[8];
                var outIdx = 0;
                Array.Copy(BitConverter.GetBytes((uint)MessageType.DsusVersionRsp), 0, outputData, outIdx, 4);
                outIdx += 4;
                Array.Copy(BitConverter.GetBytes(MaxProtocolVersion), 0, outputData, outIdx, 2);
                outIdx += 2;
                outputData[outIdx++] = 0;
                outputData[outIdx++] = 0;

                replies.Add(outputData);
                break;
            }
            case (uint)MessageType.DsucListPorts:
            {
                // Requested information on gamepads - return MAC address
                var numPadRequests = BitConverter.ToInt32(localMsg.Slice(currIdx, 4));
                currIdx += 4;
                if (numPadRequests > 0 && numPadRequests + currIdx <= localMsg.Length)
                {
                    var outputData = new byte[16];

                    lock (_controllers)
                    {
                        for (byte i = 0; i < numPadRequests; i++)
                        {
                            var currRequest = localMsg[currIdx + i];
                            if (currRequest >= _controllers.Count)
                            {
                                continue;
                            }

                            var padData = _controllers[currRequest];

                            var outIdx = 0;
                            Array.Copy(
                                BitConverter.GetBytes((uint)MessageType.DsusPortInfo),
                                0,
                                outputData,
                                outIdx,
                                4
                            );
                            outIdx += 4;

                            outputData[outIdx++] = (byte)padData.PadId;
                            outputData[outIdx++] = (byte)ControllerState.Connected;
                            outputData[outIdx++] = (byte)ControllerModel.DS4;
                            outputData[outIdx++] = (byte)(padData.IsUSB ? ControllerConnection.USB : ControllerConnection.Bluetooth);

                            var addressBytes = padData.PadMacAddress.GetAddressBytes();
                            if (addressBytes.Length == 6)
                            {
                                outputData[outIdx++] = addressBytes[0];
                                outputData[outIdx++] = addressBytes[1];
                                outputData[outIdx++] = addressBytes[2];
                                outputData[outIdx++] = addressBytes[3];
                                outputData[outIdx++] = addressBytes[4];
                                outputData[outIdx++] = addressBytes[5];
                            }
                            else
                            {
                                outputData[outIdx++] = 0;
                                outputData[outIdx++] = 0;
                                outputData[outIdx++] = 0;
                                outputData[outIdx++] = 0;
                                outputData[outIdx++] = 0;
                                outputData[outIdx++] = 0;
                            }

                            outputData[outIdx++] = (byte)GetBattery(padData);
                            outputData[outIdx++] = 0;

                            replies.Add(outputData);
                        }
                    }
                }
                break;
            }
            case (uint)MessageType.DsucPadDataReq:
            {
                if (currIdx + 8 <= localMsg.Length)
                {
                    var regFlags = localMsg[currIdx++];
                    var idToReg = localMsg[currIdx++];
                    PhysicalAddress macToReg;
                    {
                        var macBytes = new byte[6];
                        localMsg.Slice(currIdx, macBytes.Length).CopyTo(macBytes);

                        macToReg = new PhysicalAddress(macBytes);
                    }

                    lock (_clients)
                    {
                        if (_clients.TryGetValue(clientEp, out var client))
                        {
                            client.RequestPadInfo(regFlags, idToReg, macToReg);
                        }
                        else
                        {
                            var clientTimes = new ClientRequestTimes();
                            clientTimes.RequestPadInfo(regFlags, idToReg, macToReg);
                            _clients[clientEp] = clientTimes;
                        }
                    }
                }
                break;
            }
        }

        return replies;
    }

    private async Task RunReceive(CancellationToken token)
    {
        var buffer = GC.AllocateArray<byte>(PacketSize, true);
        var bufferMem = buffer.AsMemory();

        // Do processing, continually receiving from the socket
        while (true)
        {
            try
            {
                token.ThrowIfCancellationRequested();

                var receiveResult = await _udpSock.ReceiveFromAsync(bufferMem);
                var client = (IPEndPoint)receiveResult.RemoteEndPoint;

                var repliesData = ProcessIncoming(buffer.AsSpan(0, receiveResult.ReceivedBytes), client);
                if (repliesData.Count <= 0)
                {
                    continue;
                }

                // We don't care in which order the replies are sent to the client
                var tasks = repliesData.Select(async reply => { await SendPacket(client, reply); });
                await Task.WhenAll(tasks);
            }
            catch (SocketException)
            {
                if (!token.IsCancellationRequested)
                {
                    ResetUDPSocket();
                }
                else
                {
                    // We're done
                    break;
                }
            }
        }
    }

    public void Start(IPAddress ip, int port = 26760)
    {
        if (_running)
        {
            return;
        }

        _udpSock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        try
        {
            _udpSock.Bind(new IPEndPoint(ip, port));
        }
        catch (SocketException e)
        {
            _udpSock.Close();

            _form.Log(
                $"Could not start motion server. Make sure that no other applications using the port {port} are running.", e
            );
            return;
        }

        var randomBuf = new byte[4];
        new Random().NextBytes(randomBuf);
        _serverId = BitConverter.ToUInt32(randomBuf, 0);

        _ctsTransfers = new CancellationTokenSource();

        _receiveTask = Task.Run(
            async () =>
            {
                try
                {
                    await RunReceive(_ctsTransfers.Token);
                    _form.Log("Task UDP receive finished.", Logger.LogLevel.Debug);
                }
                catch (OperationCanceledException) when (_ctsTransfers.IsCancellationRequested)
                {
                    _form.Log("Task UDP receive canceled.", Logger.LogLevel.Debug);
                }
                catch (Exception e)
                {
                    _form.Log("Task UDP receive error.", e);
                    throw;
                }
            }
        );
        _form.Log("Task UDP receive started.", Logger.LogLevel.Debug);

        _running = true;
        _form.Log($"Motion server started on {ip}:{port}.");
    }

    public async Task Stop()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        _ctsTransfers.Cancel();
        _udpSock.Close();

        await _receiveTask;
        _ctsTransfers.Dispose();

        _form.Log($"Motion server stopped.");
    }

    private void ResetUDPSocket()
    {
        const uint iocIn = 0x80000000;
        const uint iocVendor = 0x18000000;
        uint sioUdpConnreset = iocIn | iocVendor | 12;

        _udpSock.IOControl((int)sioUdpConnreset, [Convert.ToByte(false)], null);
    }

    private void ReportToBuffer(Joycon hidReport, Span<byte> outputData, ref int outIdx)
    {
        var ds4 = Joycon.MapToDualShock4Input(hidReport);

        outputData[outIdx] = 0;

        if (ds4.DPad == Controller.DpadDirection.West || ds4.DPad == Controller.DpadDirection.Northwest || ds4.DPad == Controller.DpadDirection.Southwest) outputData[outIdx] |= 0x80;
        if (ds4.DPad == Controller.DpadDirection.South || ds4.DPad == Controller.DpadDirection.Southwest || ds4.DPad == Controller.DpadDirection.Southeast) outputData[outIdx] |= 0x40;
        if (ds4.DPad == Controller.DpadDirection.East || ds4.DPad == Controller.DpadDirection.Northeast || ds4.DPad == Controller.DpadDirection.Southeast) outputData[outIdx] |= 0x20;
        if (ds4.DPad == Controller.DpadDirection.North || ds4.DPad == Controller.DpadDirection.Northwest || ds4.DPad == Controller.DpadDirection.Northeast) outputData[outIdx] |= 0x10;

        if (ds4.Options) outputData[outIdx] |= 0x08;
        if (ds4.ThumbRight) outputData[outIdx] |= 0x04;
        if (ds4.ThumbLeft) outputData[outIdx] |= 0x02;
        if (ds4.Share) outputData[outIdx] |= 0x01;

        outputData[++outIdx] = 0;

        if (ds4.Square) outputData[outIdx] |= 0x80;
        if (ds4.Cross) outputData[outIdx] |= 0x40;
        if (ds4.Circle) outputData[outIdx] |= 0x20;
        if (ds4.Triangle) outputData[outIdx] |= 0x10;

        if (ds4.ShoulderRight) outputData[outIdx] |= 0x08;
        if (ds4.ShoulderLeft) outputData[outIdx] |= 0x04;
        if (ds4.TriggerRightValue == Byte.MaxValue) outputData[outIdx] |= 0x02;
        if (ds4.TriggerLeftValue == Byte.MaxValue) outputData[outIdx] |= 0x01;

        outputData[++outIdx] = ds4.Ps ? (byte)1 : (byte)0;
        outputData[++outIdx] = ds4.Touchpad ? (byte)1 : (byte)0;

        outputData[++outIdx] = ds4.ThumbLeftX;
        outputData[++outIdx] = ds4.ThumbLeftY;
        outputData[++outIdx] = ds4.ThumbRightX;
        outputData[++outIdx] = ds4.ThumbRightY;

        //we don't have analog buttons so just use the Button enums (which give either 0 or 0xFF)
        outputData[++outIdx] = (ds4.DPad == Controller.DpadDirection.West || ds4.DPad == Controller.DpadDirection.Northwest || ds4.DPad == Controller.DpadDirection.Southwest) ? (byte)0xFF : (byte)0;
        outputData[++outIdx] = (ds4.DPad == Controller.DpadDirection.South || ds4.DPad == Controller.DpadDirection.Southwest || ds4.DPad == Controller.DpadDirection.Southeast) ? (byte)0xFF : (byte)0;
        outputData[++outIdx] = (ds4.DPad == Controller.DpadDirection.East || ds4.DPad == Controller.DpadDirection.Northeast || ds4.DPad == Controller.DpadDirection.Southeast) ? (byte)0xFF : (byte)0;
        outputData[++outIdx] = (ds4.DPad == Controller.DpadDirection.North || ds4.DPad == Controller.DpadDirection.Northwest || ds4.DPad == Controller.DpadDirection.Northeast) ? (byte)0xFF : (byte)0; ;

        outputData[++outIdx] = ds4.Square ? (byte)0xFF : (byte)0;
        outputData[++outIdx] = ds4.Cross ? (byte)0xFF : (byte)0;
        outputData[++outIdx] = ds4.Circle ? (byte)0xFF : (byte)0;
        outputData[++outIdx] = ds4.Triangle ? (byte)0xFF : (byte)0;

        outputData[++outIdx] = ds4.ShoulderRight ? (byte)0xFF : (byte)0;
        outputData[++outIdx] = ds4.ShoulderLeft ? (byte)0xFF : (byte)0;

        outputData[++outIdx] = ds4.TriggerRightValue;
        outputData[++outIdx] = ds4.TriggerLeftValue;

        ++outIdx;

        //DS4 only: touchpad points
        for (int i = 0; i < 2; i++) {
            outIdx += 6;
        }

        //motion timestamp
        BitConverter.TryWriteBytes(outputData.Slice(outIdx, 8), hidReport.Timestamp);
        outIdx += 8;

        //accelerometer
        var accel = hidReport.GetAccel();
        if (accel != Vector3.Zero)
        {
            BitConverter.TryWriteBytes(outputData.Slice(outIdx, 4), accel.Y);
            outIdx += 4;
            BitConverter.TryWriteBytes(outputData.Slice(outIdx, 4), -accel.Z);
            outIdx += 4;
            BitConverter.TryWriteBytes(outputData.Slice(outIdx, 4), accel.X);
            outIdx += 4;
        }
        else
        {
            outIdx += 12;
            Console.WriteLine("No accelerometer reported.");
        }

        //gyroscope
        var gyro = hidReport.GetGyro();
        if (gyro != Vector3.Zero)
        {
            BitConverter.TryWriteBytes(outputData.Slice(outIdx, 4), gyro.Y);
            outIdx += 4;
            BitConverter.TryWriteBytes(outputData.Slice(outIdx, 4), gyro.Z);
            outIdx += 4;
            BitConverter.TryWriteBytes(outputData.Slice(outIdx, 4), gyro.X);
            outIdx += 4;
        }
        else
        {
            outIdx += 12;
            Console.WriteLine("No gyroscope reported.");
        }
    }

    private static bool IsControllerTimedout(DateTime current, DateTime last)
    {
        return (current - last).TotalSeconds >= ControllerTimeoutSeconds;
    }

    public void NewReportIncoming(Joycon hidReport)
    {
        if (!_running)
        {
            return;
        }

        var nbClients = 0;
        var now = DateTime.UtcNow;
        Span<IPEndPoint> relevantClients = null; 

        Monitor.Enter(_clients);

        try
        {
            if (_clients.Count == 0)
            {
                return;
            }

            var relevantClientsBuffer = new IPEndPoint[_clients.Count];
            relevantClients = relevantClientsBuffer.AsSpan();

            foreach (var client in _clients)
            {
                if (!IsControllerTimedout(now, client.Value.AllPadsTime))
                {
                    relevantClients[nbClients++] = client.Key;
                }
                else if ((hidReport.PadId >= 0 && hidReport.PadId < client.Value.PadIdsTime.Length) &&
                         !IsControllerTimedout(now, client.Value.PadIdsTime[hidReport.PadId]))
                {
                    relevantClients[nbClients++] = client.Key;
                }
                else if (client.Value.PadMacsTime.ContainsKey(hidReport.PadMacAddress) &&
                         !IsControllerTimedout(now, client.Value.PadMacsTime[hidReport.PadMacAddress]))
                {
                    relevantClients[nbClients++] = client.Key;
                }
                else
                {
                    //check if this client is totally dead, and remove it if so
                    var clientOk = false;
                    foreach (var padIdTime in client.Value.PadIdsTime)
                    {
                        if (!IsControllerTimedout(now, padIdTime))
                        {
                            clientOk = true;
                            break;
                        }
                    }

                    if (clientOk)
                    {
                        continue;
                    }

                    foreach (var dict in client.Value.PadMacsTime)
                    {
                        if (!IsControllerTimedout(now, dict.Value))
                        {
                            clientOk = true;
                            break;
                        }
                    }

                    if (!clientOk)
                    {
                        _clients.Remove(client.Key);
                    }
                }
            }
        }
        finally
        {
            Monitor.Exit(_clients);
        }

        if (nbClients <= 0)
        {
            return;
        }

        relevantClients = relevantClients.Slice(0, nbClients);

        Span<byte> outputData = stackalloc byte[ReportSize];
        outputData.Clear();

        var outIdx = BeginPacket(outputData);
        BitConverter.TryWriteBytes(outputData.Slice(outIdx, 4), (uint)MessageType.DsusPadDataRsp);
        outIdx += 4;

        outputData[outIdx++] = (byte)hidReport.PadId;
        outputData[outIdx++] = (byte)ControllerState.Connected;
        outputData[outIdx++] = (byte)ControllerModel.DS4;
        outputData[outIdx++] = (byte)(hidReport.IsUSB ? ControllerConnection.USB : ControllerConnection.Bluetooth);
        {
            ReadOnlySpan<byte> padMac = hidReport.PadMacAddress.GetAddressBytes();
            foreach (var number in padMac)
            {
                outputData[outIdx++] = number;
            }
        }

        outputData[outIdx++] = (byte)GetBattery(hidReport);
        outputData[outIdx++] = 1;

        BitConverter.TryWriteBytes(outputData.Slice(outIdx, 4), hidReport.PacketCounter);
        outIdx += 4;

        ReportToBuffer(hidReport, outputData, ref outIdx);
        FinishPacket(outputData);

        try
        {
            foreach (var client in relevantClients)
            {
                _udpSock.SendTo(outputData, client);
            }
        }
        // Ignore closing
        catch (ObjectDisposedException e)
        {
            _form.Log("UDP socket disposed.", e, Logger.LogLevel.Warning);
        }
        catch (SocketException e)
        {
            _form.Log("UDP socket closed.", e, Logger.LogLevel.Warning);
        }
    }

    private static ControllerBattery GetBattery(Joycon controller)
    {
        if (controller.Charging)
        {
            return ControllerBattery.Charging;
        }

        return controller.Battery switch
        {
            Joycon.BatteryLevel.Critical => ControllerBattery.Critical,
            Joycon.BatteryLevel.Low => ControllerBattery.Low,
            Joycon.BatteryLevel.Medium => ControllerBattery.Medium,
            Joycon.BatteryLevel.Full => ControllerBattery.Full,
            _ => ControllerBattery.Empty,
        };
    }

    private static int CalculateCrc32(ReadOnlySpan<byte> data, Span<byte> crc)
    {
        return Crc32.Hash(data, crc);
    }

    private static uint CalculateCrc32(ReadOnlySpan<byte> data)
    {
        Span<byte> crc = stackalloc byte[4];
        Crc32.Hash(data, crc);
        return BitConverter.ToUInt32(crc);
    }

    private class ClientRequestTimes
    {
        public ClientRequestTimes()
        {
            AllPadsTime = DateTime.MinValue;
            PadIdsTime = new DateTime[8];

            for (var i = 0; i < PadIdsTime.Length; i++)
            {
                PadIdsTime[i] = DateTime.MinValue;
            }

            PadMacsTime = new Dictionary<PhysicalAddress, DateTime>();
        }

        public DateTime AllPadsTime { get; private set; }
        public DateTime[] PadIdsTime { get; }
        public Dictionary<PhysicalAddress, DateTime> PadMacsTime { get; }

        public void RequestPadInfo(byte regFlags, byte idToReg, PhysicalAddress macToReg)
        {
            var now = DateTime.UtcNow;

            if (regFlags == 0)
            {
                AllPadsTime = now;
            }
            else
            {
                //id valid
                if ((regFlags & 0x01) != 0 && idToReg < PadIdsTime.Length)
                {
                    PadIdsTime[idToReg] = now;
                }

                //mac valid
                if ((regFlags & 0x02) != 0)
                {
                    PadMacsTime[macToReg] = now;
                }
            }
        }
    }
}
