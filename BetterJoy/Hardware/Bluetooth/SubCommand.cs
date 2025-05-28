namespace BetterJoy.Hardware.Bluetooth
{
    public enum SubCommand
    {
        GetControllerState              = 0x00,
        ManualBluetoothPairing          = 0x01,
        RequestDeviceInfo               = 0x02,
        SetReportMode                   = 0x03,
        EnableTriggersElapsedTrigger    = 0x04,
        GetPageListState                = 0x05,
        SetHciState                     = 0x06,
        ErasePairingInfo                = 0x07,
        LowPowerMode                    = 0x08,

        SpiFlashRead                    = 0x10,
        SpiFlashWrite                   = 0x11,

        ResetMcu                        = 0x20,
        SetMcuConfig                    = 0x21,
        SetMcuState                     = 0x22,

        SetPlayerLights                 = 0x30,
        GetPlayerLights                 = 0x31,
        SetHomeLight                    = 0x38,

        EnableImu                       = 0x40,
        SetImuSensitivity               = 0x41,
        WriteImuRegister                = 0x42,
        ReadImuRegister                 = 0x43,

        EnableVibration                 = 0x48,

        GetRegulatedVoltage             = 0x50
    }
}
