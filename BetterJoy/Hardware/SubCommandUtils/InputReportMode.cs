namespace BetterJoy.Hardware.SubCommandUtils;

public enum InputReportMode : byte
{
#pragma warning disable IDE0055 // Disable formatting
    ActiveNFCIRData          = 0x00,
    ActiveNFCIRConfig        = 0x01,
    ActiveNFCIRDataAndConfig = 0x02,
    ActiveIRData             = 0x03,
    MCUUpdateState           = 0x23,
    StandardFull             = 0x30,
    NFCIRPush                = 0x31,
    Unknown33                = 0x33,
    Unknown35                = 0x35,
    SimpleHID                = 0x3F,
    USBHID                   = 0x81
#pragma warning restore IDE0055
}
