namespace BetterJoy.Config;

public class MainFormConfig : Config
{
    public bool AllowCalibration;

    public MainFormConfig(Logger logger) : base(logger) { }

    public MainFormConfig(MainFormConfig config) : base(config._logger)
    {
        AllowCalibration = config.AllowCalibration;
    }

    public override void Update()
    {
        UpdateSetting("AllowCalibration", ref AllowCalibration, true);
    }

    public override MainFormConfig Clone()
    {
        return new MainFormConfig(this);
    }
}