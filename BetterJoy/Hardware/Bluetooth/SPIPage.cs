using System;

namespace BetterJoy.Hardware.Bluetooth
{
    public class SPIPage
    {
        private const byte HighAddressIndex = 1;
        private const byte LowAddressIndex = 0;
        private const byte PageSizeIndex = 4;
        private readonly byte[] _raw;
        
        public byte HighAddress => _raw[HighAddressIndex];
        public byte LowAddress => _raw[LowAddressIndex];
        public byte PageSize => _raw[PageSizeIndex];

        private SPIPage(byte high, byte low, byte len)
        {
            _raw = [high, low, 0x00, 0x00, len];
        }
        
        public static implicit operator ReadOnlySpan<byte>(SPIPage page) => page._raw;
        
        // Calibration pages
        public static readonly SPIPage UserStickCalibration = new(0x80, 0x10, 0x16);
        public static readonly SPIPage FactoryStickCalibration = new(0x60, 0x3D, 0x12);
        
        public static readonly SPIPage StickBiasLeft = new(0x60, 0x86, 16);
        
        public static readonly SPIPage StickBiasRight = new(0x60, 0x98, 16);
        
        public static readonly SPIPage UserMotionCalibration = new(0x80, 0x26, 0x1A);
        public static readonly SPIPage FactoryMotionCalibration = new(0x60, 0x20, 0x18);
    }
}
