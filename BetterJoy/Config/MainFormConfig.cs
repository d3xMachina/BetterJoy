namespace BetterJoy.Config;

public class MainFormConfig : Config
{
    public bool AllowCalibration = true;

    public MainFormConfig(Logger logger) : base(logger) { }

    public MainFormConfig(MainFormConfig config) : base(config._logger)
    {
        AllowCalibration = config.AllowCalibration;
    }

    public override void Update()
    {
        TryUpdateSetting("AllowCalibration", ref AllowCalibration);
    }

    public override MainFormConfig Clone()
    {
        return new MainFormConfig(this);
    }
}
