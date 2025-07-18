using BetterJoy.Controller;
using BetterJoy.Controller.Mapping;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace BetterJoy.Network.Server;

public class UdpServer
{
    private enum RequestType : uint
    {
        DsucVersion = 0x100000,
        DsucListPorts = 0x100001,
        DsucPadData = 0x100002
    }

    private enum ResponseType : uint
    {
        DsusVersion = 0x100000,
        DsusPortInfo = 0x100001,
        DsusPadData = 0x100002
    }

    private const ushort MaxProtocolVersion = 1001;
    private const int PacketSize = 1024;
    private const int ReportSize = 100;
    private const int PacketHeaderSize = 16;
    private const int ControllerTimeoutSeconds = 5;

    public bool HasClients => _hasClients;

    private readonly Dictionary<SocketAddress, ClientRequestTimes> _clients = [];
    private readonly IList<Joycon> _controllers;

    [MemberNotNullWhen(returnValue: true,
        nameof(_ctsTransfers),
        nameof(_receiveTask),
        nameof(_sendTask),
        nameof(_channelSendData),
        nameof(_udpSock),
        nameof(_channelSendData))]
    private bool _running { get; set; } = false;

    private volatile bool _hasClients = false;
    private CancellationTokenSource? _ctsTransfers;
    private Task? _receiveTask;
    private Task? _sendTask;
    private Channel<IPendingData>? _channelSendData;

    private uint _serverId;
    private Socket? _udpSock;

    private readonly Logger? _logger;

    public UdpServer(Logger? logger, IList<Joycon> p)
    {
        _controllers = p;
        _logger = logger;
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

        // Payload data size, we add it in FinishPacket
        currIdx += 2;

        // CRC
        currIdx += 4;

        BitConverter.TryWriteBytes(packetBuffer.Slice(currIdx, 4), _serverId);
        currIdx += 4;

        return currIdx;
    }

    private static void FinishPacket(Span<byte> packetBuffer)
    {
        // Payload size
        BitConverter.TryWriteBytes(packetBuffer.Slice(6, 2), (ushort)(packetBuffer.Length - PacketHeaderSize));

        UpdatePacketCRC(packetBuffer);
    }

    private static void UpdatePacketCRC(Span<byte> packetBuffer)
    {
        // Clear CRC bytes to not include them in the CRC calculation
        packetBuffer.Slice(8, 4).Clear();

        UdpUtils.CalculateCrc32(packetBuffer, packetBuffer.Slice(8, 4));
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

        packetSize += PacketHeaderSize;
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

        var crcCalc = UdpUtils.CalculateCrc32(localMsg[..(int)packetSize]);
        if (crcValue != crcCalc)
        {
            return false;
        }

        return true;
    }

    private static PendingReply? ProcessIncoming(Span<byte> localMsg, SocketAddress clientSocketAddress)
    {
        PendingReply? pendingReply = null;

        if (!CheckIncomingValidity(localMsg, out var currIdx))
        {
            return pendingReply;
        }

        // uint clientId = BitConverter.ToUInt32(localMsg.Slice(currIdx, 4));
        currIdx += 4;

        var messageType = BitConverter.ToUInt32(localMsg.Slice(currIdx, 4));
        currIdx += 4;

        switch (messageType)
        {
            case (uint)RequestType.DsucVersion:
            {
                pendingReply = new PendingReplyVersion(CloneSocketAddress(clientSocketAddress));
                break;
            }
            case (uint)RequestType.DsucListPorts:
            {
                // Requested information on gamepads - return MAC address
                var numPadRequests = BitConverter.ToInt32(localMsg.Slice(currIdx, 4));
                currIdx += 4;

                if (numPadRequests > 0 && numPadRequests + currIdx <= localMsg.Length)
                {
                    var padsRequested = localMsg.Slice(currIdx, numPadRequests);

                    pendingReply = new PendingReplyListPorts(
                        CloneSocketAddress(clientSocketAddress),
                        padsRequested
                    );
                }
                break;
            }
            case (uint)RequestType.DsucPadData:
            {
                if (currIdx + 8 <= localMsg.Length)
                {
                    var regFlags = localMsg[currIdx++];
                    var idToReg = localMsg[currIdx++];
                    var macAddress = localMsg.Slice(currIdx, 6);

                    pendingReply = new PendingReplyPadData(
                        CloneSocketAddress(clientSocketAddress),
                        regFlags,
                        idToReg,
                        macAddress
                    );
                }
                break;
            }
        }

        return pendingReply;
    }

    private static SocketAddress CloneSocketAddress(SocketAddress socketAddress)
    {
        var newSocketAddress = new SocketAddress(socketAddress.Family, socketAddress.Size);
        socketAddress.Buffer.CopyTo(newSocketAddress.Buffer);

        return newSocketAddress;
    }

    private async Task RunReceive(CancellationToken token)
    {
        if (!_running)
        {
            _logger?.Log("Server is not running!", Logger.LogLevel.Warning);
            return;
        }
        
        var buffer = GC.AllocateArray<byte>(PacketSize, true);
        var bufferMem = buffer.AsMemory();
        var receivedAddress = new SocketAddress(_udpSock.AddressFamily);

        // Do processing, continually receiving from the socket
        while (true)
        {
            try
            {
                token.ThrowIfCancellationRequested();

                var receivedBytes = await _udpSock.ReceiveFromAsync(bufferMem, SocketFlags.None, receivedAddress, token);
                var pendingReply = ProcessIncoming(buffer.AsSpan(0, receivedBytes), receivedAddress);

                if (pendingReply != null)
                {
                    await _channelSendData.Writer.WriteAsync(pendingReply, token);
                }
            }
            catch (SocketException)
            {
                if (!token.IsCancellationRequested)
                {
                    ResetUDPSocket();
                }
            }
        }
    }

    private async Task RunSend(CancellationToken token)
    {
        if (!_running)
        {
            _logger?.Log("Server is not running!", Logger.LogLevel.Warning);
            return;
        }
        
        var buffer = GC.AllocateArray<byte>(ReportSize, true);
        var bufferMem = buffer.AsMemory();

        var channelReader = _channelSendData.Reader;

        while (await channelReader.WaitToReadAsync(token))
        {
            while (channelReader.TryRead(out var pendingData))
            {
                token.ThrowIfCancellationRequested();

                switch (pendingData)
                {
                    case PendingReport pendingReport:
                    {
                        var report = pendingReport.Report;
                        var clients = GetAndUpdateClients(report);

                        if (clients.Count > 0)
                        {
                            var packetLength = MakeControllerReportPacket(report, buffer);
                            int nbPackets = report.DeltaPackets > 0 ? report.Motion.Length : 1;

                            for (int i = 0; i < nbPackets; ++i)
                            {
                                AddMotionToControllerReportPacket(report, i, buffer);

                                foreach (var client in clients)
                                {
                                    await SendData(bufferMem[..packetLength], client, token);
                                }
                            }
                        }
                        break;
                    }
                    case PendingReplyListPorts listPortsReply:
                    {
                        var controllersSafe = new List<Joycon>(_controllers);

                        foreach (var padRequested in listPortsReply.PadsRequested)
                        {
                            if (padRequested >= controllersSafe.Count)
                            {
                                continue;
                            }

                            var packetLength = MakeReplyListPortPacket(controllersSafe[padRequested], buffer);
                            await SendData(bufferMem[..packetLength], listPortsReply.Client, token);
                        }
                        break;
                    }
                    case PendingReplyPadData padDataReply:
                    {
                        RequestPadData(padDataReply.Client, padDataReply.RegFlags, padDataReply.IdToReg, padDataReply.MacToReg);
                        break;
                    }
                    case PendingReplyVersion versionReply:
                    {
                        var packetLength = MakeReplyVersionPacket(buffer);
                        await SendData(bufferMem[..packetLength], versionReply.Client, token);
                        break;
                    }
                    default:
                    {
                        throw new NotImplementedException($"Pending data {pendingData} is not implemented.");
                    }
                }
            }
        }
    }

    private List<SocketAddress> GetAndUpdateClients(UdpControllerReport report)
    {
        var relevantClients = new List<SocketAddress>();
        var now = DateTime.UtcNow;

        if (_clients.Count == 0)
        {
            return relevantClients;
        }

        foreach (var client in _clients)
        {
            var controllerAlive = false;

            if (!IsControllerTimedout(now, client.Value.AllPadsTime))
            {
                controllerAlive = true;
            }
            else if (report.PadId >= 0 && report.PadId < client.Value.PadIdsTime.Length &&
                     !IsControllerTimedout(now, client.Value.PadIdsTime[report.PadId]))
            {
                controllerAlive = true;
            }
            else if (client.Value.PadMacsTime.TryGetValue(report.MacAddress, out var padMacTime) &&
                     !IsControllerTimedout(now, padMacTime))
            {
                controllerAlive = true;
            }

            if (controllerAlive)
            {
                relevantClients.Add(client.Key);
                continue;
            }

            // Check if this client is totally dead, and remove it if so
            var clientAlive = false;
            foreach (var padIdTime in client.Value.PadIdsTime)
            {
                if (!IsControllerTimedout(now, padIdTime))
                {
                    clientAlive = true;
                    break;
                }
            }

            if (clientAlive)
            {
                continue;
            }

            foreach (var padMacTime in client.Value.PadMacsTime.Values)
            {
                if (!IsControllerTimedout(now, padMacTime))
                {
                    clientAlive = true;
                    break;
                }
            }

            if (!clientAlive)
            {
                _clients.Remove(client.Key);
                if (_clients.Count == 0)
                {
                    _hasClients = false;
                }
            }
        }

        return relevantClients;
    }

    private void RequestPadData(SocketAddress clientSocketAddress, byte regFlags, byte idToReg, MacAddress macToReg)
    {
        if (_clients.TryGetValue(clientSocketAddress, out var client))
        {
            client.RequestPadInfo(regFlags, idToReg, macToReg);
        }
        else
        {
            var clientTimes = new ClientRequestTimes();
            clientTimes.RequestPadInfo(regFlags, idToReg, macToReg);

            _clients[clientSocketAddress] = clientTimes;
            _hasClients = true;
        }
    }

    private int MakeControllerReportPacket(UdpControllerReport report, Span<byte> outputData)
    {
        outputData.Clear();

        var outIdx = BeginPacket(outputData);

        BitConverter.TryWriteBytes(outputData.Slice(outIdx, 4), (uint)ResponseType.DsusPadData);
        outIdx += 4;

        outputData[outIdx++] = (byte)report.PadId;
        outputData[outIdx++] = (byte)ControllerState.Connected;
        outputData[outIdx++] = (byte)ControllerModel.DS4;
        outputData[outIdx++] = (byte)report.ConnectionType;

        ReadOnlySpan<byte> mac = report.MacAddress;
        mac.CopyTo(outputData[outIdx..]);
        outIdx += 6;

        outputData[outIdx++] = (byte)report.Battery;
        outputData[outIdx++] = 1;

        // Packet counter
        outIdx += 4;

        var ds4 = report.Input;

        outputData[outIdx] = 0;

        if (ds4.DPad == DpadDirection.West || ds4.DPad == DpadDirection.Northwest || ds4.DPad == DpadDirection.Southwest) outputData[outIdx] |= 0x80;
        if (ds4.DPad == DpadDirection.South || ds4.DPad == DpadDirection.Southwest || ds4.DPad == DpadDirection.Southeast) outputData[outIdx] |= 0x40;
        if (ds4.DPad == DpadDirection.East || ds4.DPad == DpadDirection.Northeast || ds4.DPad == DpadDirection.Southeast) outputData[outIdx] |= 0x20;
        if (ds4.DPad == DpadDirection.North || ds4.DPad == DpadDirection.Northwest || ds4.DPad == DpadDirection.Northeast) outputData[outIdx] |= 0x10;

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
        if (ds4.TriggerRightValue == byte.MaxValue) outputData[outIdx] |= 0x02;
        if (ds4.TriggerLeftValue == byte.MaxValue) outputData[outIdx] |= 0x01;

        outputData[++outIdx] = ds4.Ps ? (byte)1 : (byte)0;
        outputData[++outIdx] = ds4.Touchpad ? (byte)1 : (byte)0;

        outputData[++outIdx] = ds4.ThumbLeftX;
        outputData[++outIdx] = ds4.ThumbLeftY;
        outputData[++outIdx] = ds4.ThumbRightX;
        outputData[++outIdx] = ds4.ThumbRightY;

        // We don't have analog buttons so just use the Button enums (which give either 0 or 0xFF)
        outputData[++outIdx] = ds4.DPad == DpadDirection.West || ds4.DPad == DpadDirection.Northwest || ds4.DPad == DpadDirection.Southwest ? (byte)0xFF : (byte)0;
        outputData[++outIdx] = ds4.DPad == DpadDirection.South || ds4.DPad == DpadDirection.Southwest || ds4.DPad == DpadDirection.Southeast ? (byte)0xFF : (byte)0;
        outputData[++outIdx] = ds4.DPad == DpadDirection.East || ds4.DPad == DpadDirection.Northeast || ds4.DPad == DpadDirection.Southeast ? (byte)0xFF : (byte)0;
        outputData[++outIdx] = ds4.DPad == DpadDirection.North || ds4.DPad == DpadDirection.Northwest || ds4.DPad == DpadDirection.Northeast ? (byte)0xFF : (byte)0; ;

        outputData[++outIdx] = ds4.Square ? (byte)0xFF : (byte)0;
        outputData[++outIdx] = ds4.Cross ? (byte)0xFF : (byte)0;
        outputData[++outIdx] = ds4.Circle ? (byte)0xFF : (byte)0;
        outputData[++outIdx] = ds4.Triangle ? (byte)0xFF : (byte)0;

        outputData[++outIdx] = ds4.ShoulderRight ? (byte)0xFF : (byte)0;
        outputData[++outIdx] = ds4.ShoulderLeft ? (byte)0xFF : (byte)0;

        outputData[++outIdx] = ds4.TriggerRightValue;
        outputData[++outIdx] = ds4.TriggerLeftValue;

        ++outIdx;

        // DS4 only: touchpad points
        for (int i = 0; i < 2; i++)
        {
            outIdx += 6;
        }

        // Motion timestamp
        outIdx += 8;

        // Accelerometer
        outIdx += 12;

        // Gyroscope
        outIdx += 12;

        FinishPacket(outputData[..outIdx]);

        return outIdx;
    }

    private static void AddMotionToControllerReportPacket(UdpControllerReport report, int packetNumber, Span<byte> outputData)
    {
        int outIdx = 32;

        // Update Packet counter
        BitConverter.TryWriteBytes(outputData.Slice(outIdx, 4), report.PacketCounter + packetNumber);
        outIdx += 4;

        // Input
        outIdx += 32;

        // Update timestamp
        ulong timestamp = report.Timestamp + ((ulong)packetNumber * report.DeltaPackets);
        BitConverter.TryWriteBytes(outputData.Slice(outIdx, 8), timestamp);
        outIdx += 8;

        ref var motion = ref report.Motion[packetNumber];

        // Accelerometer
        var accel = motion.Accelerometer;
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
        }

        // Gyroscope
        var gyro = motion.Gyroscope;
        if (gyro != Vector3.Zero)
        {
            BitConverter.TryWriteBytes(outputData.Slice(outIdx, 4), gyro.Y);
            outIdx += 4;
            BitConverter.TryWriteBytes(outputData.Slice(outIdx, 4), gyro.Z);
            outIdx += 4;
            BitConverter.TryWriteBytes(outputData.Slice(outIdx, 4), gyro.X);
        }

        UpdatePacketCRC(outputData);
    }

    private int MakeReplyListPortPacket(Joycon controller, Span<byte> outputData)
    {
        outputData.Clear();

        var outIdx = BeginPacket(outputData);

        BitConverter.TryWriteBytes(outputData.Slice(outIdx, 4), (uint)ResponseType.DsusPortInfo);
        outIdx += 4;

        outputData[outIdx++] = (byte)controller.PadId;
        outputData[outIdx++] = (byte)ControllerState.Connected;
        outputData[outIdx++] = (byte)ControllerModel.DS4;
        outputData[outIdx++] = (byte)(controller.IsUSB ? ControllerConnection.USB : ControllerConnection.Bluetooth);

        ReadOnlySpan<byte> macAddress = controller.MacAddress;
        if (macAddress.Length == 6)
        {
            macAddress.CopyTo(outputData.Slice(outIdx, macAddress.Length));
        }

        outIdx += 6;

        outputData[outIdx++] = (byte)UdpUtils.GetBattery(controller);
        outputData[outIdx++] = 0;

        FinishPacket(outputData[..outIdx]);

        return outIdx;
    }

    private int MakeReplyVersionPacket(Span<byte> outputData)
    {
        outputData.Clear();

        var outIdx = BeginPacket(outputData);

        BitConverter.TryWriteBytes(outputData.Slice(outIdx, 4), (uint)ResponseType.DsusVersion);
        outIdx += 4;
        BitConverter.TryWriteBytes(outputData.Slice(outIdx, 2), MaxProtocolVersion);
        outIdx += 2;
        outputData[outIdx++] = 0;
        outputData[outIdx++] = 0;

        FinishPacket(outputData[..outIdx]);

        return outIdx;
    }

    private async ValueTask SendData(ReadOnlyMemory<byte> outputData, SocketAddress client, CancellationToken cancellationToken)
    {
        if (!_running)
        {
            _logger?.Log("Server is not running!", Logger.LogLevel.Warning);
            return;
        }
        
        try
        {
            await _udpSock.SendToAsync(outputData, SocketFlags.None, client, cancellationToken);
        }
        // Ignore closing
        catch (SocketException e)
        {
            _logger?.Log("UDP socket closed.", e, Logger.LogLevel.Warning);
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

            _logger?.Log(
                $"Could not start motion server. Make sure that no other applications using the port {port} are running.", e
            );
            return;
        }

        var randomBuf = new byte[4];
        new Random().NextBytes(randomBuf);
        _serverId = BitConverter.ToUInt32(randomBuf, 0);

        _ctsTransfers = new CancellationTokenSource();

        _channelSendData = Channel.CreateBounded<IPendingData>(
            new BoundedChannelOptions(100)
            {
                SingleWriter = false,
                SingleReader = true,
                FullMode = BoundedChannelFullMode.DropOldest
            }
        );

        _receiveTask = Task.Run(
            async () =>
            {
                try
                {
                    await RunReceive(_ctsTransfers.Token);
                    _logger?.Log("Task UDP receive finished.", Logger.LogLevel.Debug);
                }
                catch (OperationCanceledException) when (_ctsTransfers.IsCancellationRequested)
                {
                    _logger?.Log("Task UDP receive canceled.", Logger.LogLevel.Debug);
                }
                catch (Exception e)
                {
                    _logger?.Log("Task UDP receive error.", e);
                    throw;
                }
            }
        );
        _logger?.Log("Task UDP receive started.", Logger.LogLevel.Debug);

        _sendTask = Task.Run(
            async () =>
            {
                try
                {
                    await RunSend(_ctsTransfers.Token);
                    _logger?.Log("Task UDP send finished.", Logger.LogLevel.Debug);
                }
                catch (OperationCanceledException) when (_ctsTransfers.IsCancellationRequested)
                {
                    _logger?.Log("Task UDP send canceled.", Logger.LogLevel.Debug);
                }
                catch (Exception e)
                {
                    _logger?.Log("Task UDP send error.", e);
                    throw;
                }
            }
        );
        _logger?.Log("Task UDP send started.", Logger.LogLevel.Debug);

        _running = true;
        _logger?.Log($"Motion server started on {ip}:{port}.");
    }

    public async Task Stop()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        _hasClients = false;
        _channelSendData.Writer.Complete();
        _ctsTransfers.Cancel();
        _udpSock.Close();

        await Task.WhenAll(_receiveTask, _sendTask);
        _ctsTransfers.Dispose();

        _clients.Clear();

        _logger?.Log("Motion server stopped.");
    }

    private void ResetUDPSocket()
    {
        if (!_running)
        {
            _logger?.Log("Server is not running!", Logger.LogLevel.Warning);
            return;
        }
        
        const uint IocIn = 0x80000000;
        const uint IocVendor = 0x18000000;
        uint sioUdpConnreset = IocIn | IocVendor | 12;

        _udpSock.IOControl((int)sioUdpConnreset, [Convert.ToByte(false)], null);
    }

    private static bool IsControllerTimedout(DateTime current, DateTime last)
    {
        return (current - last).TotalSeconds >= ControllerTimeoutSeconds;
    }

    public void SendControllerReport(UdpControllerReport report)
    {
        if (!_hasClients || !_running)
        {
            return;
        }

        var pendingReport = new PendingReport(report);

        while (!_channelSendData.Writer.TryWrite(pendingReport) && _hasClients) { }
    }

    private class ClientRequestTimes
    {
        public DateTime AllPadsTime { get; private set; }
        public DateTime[] PadIdsTime { get; }
        public Dictionary<MacAddress, DateTime> PadMacsTime { get; }

        public ClientRequestTimes()
        {
            AllPadsTime = DateTime.MinValue;
            PadIdsTime = new DateTime[8];

            for (var i = 0; i < PadIdsTime.Length; i++)
            {
                PadIdsTime[i] = DateTime.MinValue;
            }

            PadMacsTime = [];
        }

        public void RequestPadInfo(byte regFlags, byte idToReg, MacAddress macToReg)
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

    private interface IPendingData { }

    private class PendingReport : IPendingData
    {
        public readonly UdpControllerReport Report;

        public PendingReport(UdpControllerReport report)
        {
            Report = report;
        }
    }

    private abstract class PendingReply : IPendingData
    {
        public readonly SocketAddress Client;

        public PendingReply(SocketAddress client)
        {
            Client = client;
        }
    }

    private class PendingReplyVersion : PendingReply
    {
        public PendingReplyVersion(SocketAddress client) : base(client) { }
    }

    private class PendingReplyListPorts : PendingReply
    {
        public readonly byte[] PadsRequested;

        public PendingReplyListPorts(SocketAddress client, ReadOnlySpan<byte> padsRequested) : base(client)
        {
            PadsRequested = new byte[padsRequested.Length];
            padsRequested.CopyTo(PadsRequested);
        }
    }

    private class PendingReplyPadData : PendingReply
    {
        public byte RegFlags;
        public byte IdToReg;
        public MacAddress MacToReg;

        public PendingReplyPadData(SocketAddress client, byte regFlags, byte idToReg, ReadOnlySpan<byte> macAddress) : base(client)
        {
            if (macAddress.Length != 6)
            {
                throw new ArgumentException($"{nameof(PendingReplyPadData)} expects 6 bytes for the MAC address.");
            }

            RegFlags = regFlags;
            IdToReg = idToReg;
            macAddress.CopyTo(MacToReg);
        }
    }
}
