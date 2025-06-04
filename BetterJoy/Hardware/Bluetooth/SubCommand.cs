namespace BetterJoy.Hardware.Bluetooth;

public enum SubCommand : byte
{
    GetControllerState              = 0x00,
    ManualBluetoothPairing          = 0x01,
    RequestDeviceInfo               = 0x02,
    SetReportMode                   = 0x03,
    GetTriggerButtonsElapsedTime    = 0x04,
    GetPageListState                = 0x05,
    SetHCIState                     = 0x06,
    ErasePairingInfo                = 0x07,
    EnableLowPowerMode              = 0x08,

    SPIFlashRead                    = 0x10,
    SPIFlashWrite                   = 0x11,

    ResetMCU                        = 0x20,
    SetMCUConfig                    = 0x21,
    SetMCUState                     = 0x22,

    SetPlayerLights                 = 0x30,
    GetPlayerLights                 = 0x31,
    SetHomeLight                    = 0x38,

    EnableIMU                       = 0x40,
    SetIMUSensitivity               = 0x41,
    WriteIMURegister                = 0x42,
    ReadIMURegister                 = 0x43,

    EnableVibration                 = 0x48,

    GetRegulatedVoltage             = 0x50
}
