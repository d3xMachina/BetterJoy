namespace BetterJoy.Hardware.SubCommandUtils;

public enum InputReportMode : byte
{
    ActiveNFCIRData          = 0x00,
    ActiveNFCIRConfig        = 0x01,
    ActiveNFCIRDataAndConfig = 0x02,
    ActiveIRData             = 0x03,
    MCUUpdateState           = 0x23,
    StandardFull60Hz         = 0x30,
    NFCIRPush60Hz            = 0x31,
    Unknown33                = 0x33,
    Unknown35                = 0x35,
    SimpleHID                = 0x3F,
    USBHID                   = 0x81
}
