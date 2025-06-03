#nullable enable
using System;
using System.Linq;
using System.Text;

namespace BetterJoy.Hardware.Bluetooth
{
    public class Request
    {
        private static readonly byte[] StopRumbleBuf = [0x0, 0x1, 0x40, 0x40, 0x0, 0x1, 0x40, 0x40]; // Stop rumble
        
        private const int RequestStartIndex = 0;
        private const int CommandCountIndex = 1;
        private const int RumbleContentsStartIndex = 2;
        private const int CommandIndex = 10;
        private const int ArgumentsStartIndex = 11;
        private const int BluetoothMessageLength = 49;

        private const int RumbleLength = CommandIndex - RumbleContentsStartIndex;
        private const int MaxArgsLength = BluetoothMessageLength - ArgumentsStartIndex;
        
        private readonly int _argsLength;
        private readonly byte[] _raw = new byte[BluetoothMessageLength];
        private string? _cachedStringRepresentation;
        
        public Request(SubCommand subCommand, uint commandCount, ReadOnlySpan<byte> args = default, ReadOnlySpan<byte> rumble = default)
        {
            // Default to stopping the rumble
            if (rumble.IsEmpty)
            {
                rumble = StopRumbleBuf;
            }
            else if (rumble.Length != RumbleLength) // Check the rumble length if user provided
            {
                throw new ArgumentException($@"Rumble span is not correct size. Expected: {RumbleLength} Received: {rumble.Length}", nameof(rumble));
            }

            // Check the args length
            if (args.Length > MaxArgsLength)
            {
                throw new ArgumentException($@"Args span is too large. Expected at most: {RumbleLength} Received: {rumble.Length}", nameof(args));
            }
            
            _argsLength = args.Length;
            _raw[RequestStartIndex] = 0x01; // Always
            _raw[CommandCountIndex] = (byte)(commandCount & 0x0F); // Command index only uses 4 bits
            _raw[CommandIndex] = (byte)subCommand;
            
            rumble.CopyTo(_raw.AsSpan(RumbleContentsStartIndex));
            args.CopyTo(_raw.AsSpan(ArgumentsStartIndex));
        }
        
        public static implicit operator ReadOnlySpan<byte>(Request request) => request._raw;
        
         public override string ToString() => _cachedStringRepresentation ??= BuildString();
        
        private string BuildString()
        {
            var output = new StringBuilder();

            output.Append($"Subcommand {_raw[CommandIndex]:X2} sent.");
            
            if (_argsLength > 0)
            {
                output.Append(" Data: ");
                
                output.AppendJoin(' ', _raw.Skip(ArgumentsStartIndex).Take(_argsLength).Select(b => $"{b:X2}"));
            }
            
            return output.ToString();
        }
    }
}
