namespace BetterJoy.Config;

public class MainFormConfig : Config
{
    public bool AllowCalibration;

    public MainFormConfig(MainForm form) : base(form) { }

    public MainFormConfig(MainFormConfig config) : base(config._form)
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